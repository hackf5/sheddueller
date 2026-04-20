# Sheddueller v1 Specification

Status: Accepted for implementation  
Last updated: 2026-04-19

## Summary

Sheddueller v1 is an in-process job scheduler for `net10.0`. It executes immediate, fire-and-forget jobs across any number of homogeneous application hosts that share one logical job store.

V1 is defined by four core capabilities:

- Strict numeric job priority.
- Dynamic cluster-wide concurrency groups.
- Cancellation-aware expression-based job submission against DI services.
- Backend-agnostic storage, proven first by an in-memory provider.

Hosting integration is built around the standard .NET host model:

- `HostApplicationBuilder`: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostapplicationbuilder?view=net-10.0-pp>
- `IHostedService`: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.ihostedservice?view=net-10.0-pp>

## Goals

- Execute immediate jobs submitted by application code.
- Allow callers to assign any integer priority at enqueue time.
- Enforce concurrency limits with runtime-defined group keys instead of preconfigured queues.
- Allow any number of hosts to join the same cluster and compete for work from the shared store.
- Keep the storage contract independent from any specific relational engine.
- Prove the storage contract with an in-memory provider before adding SQL-backed providers.

## Non-Goals

Non-goals are scoped to v1 unless explicitly marked permanent.

- Delayed or scheduled execution.
- Recurring or cron-based work.
- Job chaining, workflows, or dependency graphs.
- Automatic retries or configurable retry policies.
- Result storage or return-value retrieval.
- First-class observability or operator dashboards.
- Automatic recovery of work claimed by a dead node.

## Architecture

- Every application host is a homogeneous node. There is no leader role in v1.
- Each node joins the cluster by registering Sheddueller in DI and running a hosted background worker.
- All nodes share one logical `IJobStore`.
- Application code enqueues work through `IJobEnqueuer`.
- The worker claims jobs from the store, resolves the target service from DI, invokes the captured method with a scheduler-owned `CancellationToken`, and reports terminal completion back to the store.
- Nodes execute up to `MaxConcurrentExecutionsPerNode` jobs concurrently. This node-local limit is separate from cluster-wide concurrency groups.

## Public API Shape

The API names in this section are normative. Implementation may add overloads, but it must not rename or replace these primary entry points.

### Registration

```csharp
public static class ShedduellerServiceCollectionExtensions
{
    public static IServiceCollection AddSheddueller(
        this IServiceCollection services,
        Action<ShedduellerBuilder>? configure = null);
}

public static class ShedduellerHostApplicationBuilderExtensions
{
    public static HostApplicationBuilder AddSheddueller(
        this HostApplicationBuilder builder,
        Action<ShedduellerBuilder>? configure = null);
}

public sealed class ShedduellerOptions
{
    public string? NodeId { get; set; }
    public int MaxConcurrentExecutionsPerNode { get; set; } = Environment.ProcessorCount;
    public TimeSpan IdlePollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}
```

Requirements:

- `AddSheddueller` must register the scheduler services, the hosted worker, and the default app-facing interfaces.
- `NodeId` is optional. If not configured, Sheddueller must generate a unique process-instance identifier at startup.
- `MaxConcurrentExecutionsPerNode` must be a positive integer. Its default is `Environment.ProcessorCount`.
- `IdlePollingInterval` must be positive. It is the fallback poll cadence when no wake signal is received.
- The core package must register `TimeProvider.System` by default when no `TimeProvider` is already registered.
- Runtime-generated timestamps passed to `IJobStore` must come from the registered `TimeProvider`.

### Builder

```csharp
public sealed class ShedduellerBuilder
{
    public IServiceCollection Services { get; }

    public ShedduellerBuilder ConfigureOptions(
        Action<ShedduellerOptions> configure);

    public ShedduellerBuilder UseJobPayloadSerializer<TSerializer>()
        where TSerializer : class, IJobPayloadSerializer;

    public ShedduellerBuilder UseJobPayloadSerializer(
        IJobPayloadSerializer serializer);
}
```

Requirements:

- `ShedduellerBuilder` is the fluent configuration surface used by `AddSheddueller`.
- `Services` exposes the underlying service collection for provider packages.
- `ConfigureOptions` composes with other option configuration.
- `UseJobPayloadSerializer<TSerializer>` registers a singleton serializer implementation.
- `UseJobPayloadSerializer(IJobPayloadSerializer)` registers the provided serializer instance as singleton.
- If no serializer is configured, Sheddueller must use the built-in `SystemTextJsonJobPayloadSerializer`.
- Store providers must extend `ShedduellerBuilder` from their own package. V1's in-memory provider exposes `UseInMemoryStore`.
- `AddSheddueller` must fail during startup validation if no `IJobStore` provider has been registered.
- Startup validation should run before hosted workers begin claiming work.

### Job Submission

```csharp
public interface IJobEnqueuer
{
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, Task>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);
}

public sealed record JobSubmission(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null);
```

Requirements:

- The expression must be a single instance-method call on `TService`.
- `TService` must be resolvable from DI at execution time.
- Supported job methods return `Task` or `ValueTask`.
- The expression must accept the scheduler-provided `CancellationToken` and forward it to the target service method call.
- The scheduler-provided `CancellationToken` is runtime-owned and is never serialized as part of the job payload.
- Callers must not capture an external `CancellationToken` for execution control inside the submitted expression.
- V1 supports non-generic instance method calls only.
- V1 does not support open or closed generic target method calls.
- Optional/default parameter behavior is not inferred. Every target method argument except the scheduler-owned cancellation token must appear explicitly in the method call expression.
- Argument subexpressions are evaluated once at enqueue time and the resulting values are serialized.
- Method arguments are captured at enqueue time and serialized through `IJobPayloadSerializer`.
- Captured argument values must be serializer-compatible.
- `JobSubmission.Priority` is an unconstrained sortable integer. Higher values mean higher priority.
- `JobSubmission.ConcurrencyGroupKeys` is optional. Missing or empty means the job has no concurrency-group constraints.
- Group keys are opaque, case-sensitive, non-empty strings.
- Duplicate group keys in a submission must be deduplicated before persistence.

Unsupported expression forms:

- Static method calls.
- Generic method calls.
- Property access without a terminal method call.
- Lambdas containing control flow, loops, or multiple method calls.
- Captures of live service instances, delegates, cancellation tokens, streams, or other unserializable runtime-only objects.

Invalid expressions, invalid options, invalid group keys, invalid limits, missing store provider registration, and serializer failures must throw deterministic exceptions at the API or startup boundary.

### Runtime Concurrency Management

```csharp
public interface IConcurrencyGroupManager
{
    ValueTask SetLimitAsync(
        string groupKey,
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<int?> GetConfiguredLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}
```

Requirements:

- `limit` must be a positive integer.
- A missing configured limit does not mean unlimited capacity. The scheduler must treat it as an effective limit of `1`.
- Lowering a limit below the current number of claimed jobs must not preempt running work. It only blocks future claims until occupancy drops below the new limit.

## Job Model

The persisted job record must contain at least the following fields:

| Field | Type | Notes |
| --- | --- | --- |
| `JobId` | `Guid` | Stable identifier returned from `EnqueueAsync`. |
| `State` | `JobState` | One of `Queued`, `Claimed`, `Completed`, `Failed`. |
| `Priority` | `int` | Higher numbers run first. |
| `EnqueueSequence` | `long` | Store-assigned, strictly monotonic ordering key used for FIFO within equal priority. |
| `EnqueuedAtUtc` | `DateTimeOffset` | Timestamp recorded at enqueue time. |
| `ServiceType` | `string` | Assembly-qualified or otherwise unambiguous service type identity. |
| `MethodName` | `string` | Target method name. |
| `MethodParameterTypes` | `string[]` | Unambiguous parameter type identities for overload resolution. |
| `SerializedArguments` | `SerializedJobPayload` | Serializer-owned payload for method arguments. |
| `ConcurrencyGroupKeys` | `string[]` | Distinct persisted group keys for this job. |
| `ClaimedByNodeId` | `string?` | Node that claimed the job, if any. |
| `ClaimedAtUtc` | `DateTimeOffset?` | When the claim occurred. |
| `CompletedAtUtc` | `DateTimeOffset?` | When successful execution finished. |
| `FailedAtUtc` | `DateTimeOffset?` | When failed execution finished. |
| `Failure` | `JobFailureInfo?` | Best-effort error details for failed jobs. |

`JobFailureInfo` must contain:

- Exception type name.
- Exception message.
- Best-effort stack trace text.

`JobState` has the following meaning:

- `Queued`: available to be considered for claiming.
- `Claimed`: owned by a node and counted against every referenced concurrency group.
- `Completed`: terminal success state.
- `Failed`: terminal failure state.

There is no separate `Running` state in v1. A claimed job is considered active until it transitions to `Completed` or `Failed`.

## Scheduling Semantics

### Ordering

- The global candidate order is `Priority DESC, EnqueueSequence ASC`.
- FIFO is defined by `EnqueueSequence`, not by wall-clock timestamps.
- The store must assign `EnqueueSequence` atomically at enqueue time.

### Claiming

- A job may be claimed only from `Queued`.
- Claiming a job must atomically transition it to `Claimed` and reserve capacity in every referenced concurrency group.
- Two nodes must never successfully claim the same job.
- Two nodes must never oversubscribe the same concurrency group beyond its effective limit.

### Concurrency Groups

- Concurrency groups are cluster-wide across all nodes sharing the same store.
- The effective limit for a group is:
  - the configured limit from `IConcurrencyGroupManager`, when present
  - otherwise `1`
- A job with multiple group keys may start only when every group has available capacity.
- Jobs without group keys are not subject to group-based throttling.

### Scanning Behavior

- The scheduler must not stop at the first queued job if that job is blocked by concurrency limits.
- The scheduler must continue scanning queued jobs in global candidate order until it finds the first claimable job.
- This rule exists so a blocked high-priority job does not stall unrelated work indefinitely.

### Execution

- After claiming, the node resolves the target `TService` from DI.
- Each claimed job executes in a fresh `IServiceScope`.
- The worker invokes the captured method using the deserialized arguments and a scheduler-owned execution `CancellationToken`.
- On successful completion, the job transitions to `Completed`.
- On exception, the job transitions to `Failed`.
- If host shutdown cancels the execution token and user code observes it by throwing `OperationCanceledException` or returning a canceled job, v1 transitions the job to `Failed` with cancellation details because v1 has no `Canceled` state.
- V1 does not retry failed jobs.

### Worker Loop

- The hosted worker must stop claiming new jobs when host shutdown begins.
- The hosted worker must use signal-plus-poll waiting.
- Enqueueing a job must signal workers that work may be available.
- Updating a concurrency-group limit must signal workers that blocked work may now be available.
- If a signal is missed, the worker must still make progress through fallback polling using `IdlePollingInterval`.
- The wake signal is a core runtime service. It is not part of `IJobStore`.
- The worker must never execute more than `MaxConcurrentExecutionsPerNode` jobs concurrently.

### Node Failure

- If a node dies after claiming a job, the job remains in `Claimed`.
- V1 does not include lease expiration, heartbeat renewal, or automatic reclaim.
- This is an intentional limitation, not an implementation gap.

## Storage Abstraction

`IJobStore` is a first-class extension point. V1 ships with an in-memory implementation in a separate provider package, but the abstraction must be suitable for later relational providers.

```csharp
public interface IJobStore
{
    ValueTask<EnqueueJobResult> EnqueueAsync(
        EnqueueJobRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ClaimJobResult> TryClaimNextAsync(
        ClaimJobRequest request,
        CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(
        CompleteJobRequest request,
        CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(
        FailJobRequest request,
        CancellationToken cancellationToken = default);

    ValueTask SetConcurrencyLimitAsync(
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}

public sealed record EnqueueJobRequest(
    Guid JobId,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedJobPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    DateTimeOffset EnqueuedAtUtc);

public sealed record EnqueueJobResult(
    Guid JobId,
    long EnqueueSequence);

public sealed record ClaimJobRequest(
    string NodeId,
    DateTimeOffset ClaimedAtUtc);

public abstract record ClaimJobResult
{
    public sealed record Claimed(ClaimedJob Job) : ClaimJobResult;
    public sealed record NoJobAvailable() : ClaimJobResult;
}

public sealed record ClaimedJob(
    Guid JobId,
    long EnqueueSequence,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedJobPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys);

public sealed record CompleteJobRequest(
    Guid JobId,
    string NodeId,
    DateTimeOffset CompletedAtUtc);

public sealed record FailJobRequest(
    Guid JobId,
    string NodeId,
    DateTimeOffset FailedAtUtc,
    JobFailureInfo Failure);

public sealed record SetConcurrencyLimitRequest(
    string GroupKey,
    int Limit,
    DateTimeOffset UpdatedAtUtc);
```

The store contract must provide these capabilities:

- Enqueue a job and assign its `EnqueueSequence`.
- Return the next claimable job according to the scheduling rules.
- Perform claim selection atomically with the transition to `Claimed`.
- Persist terminal transitions to `Completed` and `Failed`.
- Store and retrieve configured concurrency-group limits.
- Read enough job state to enforce group occupancy across the cluster.
- The operation timestamps supplied by the runtime are authoritative for v1 stores.

Required behavioral rules:

- Claim selection and group-capacity reservation must be one atomic store operation.
- The store owns concurrency-group occupancy. The scheduler must not track cluster-wide group occupancy in memory.
- Group occupancy is the number of jobs in `Claimed` that reference a given group key.
- Terminal transitions must release all group occupancy held by that job.
- The store abstraction must not encode PostgreSQL-, MySQL-, or provider-specific concepts into app-facing APIs.

The spec does not lock v1 to a particular SQL schema. It locks the behavior and the storage responsibilities.

## Payload Serialization

`IJobPayloadSerializer` is a first-class extension point.

```csharp
public interface IJobPayloadSerializer
{
    ValueTask<SerializedJobPayload> SerializeAsync(
        IReadOnlyList<object?> arguments,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<object?>> DeserializeAsync(
        SerializedJobPayload payload,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);
}

public sealed record SerializedJobPayload(
    string ContentType,
    byte[] Data);

public sealed class SystemTextJsonJobPayloadSerializer : IJobPayloadSerializer;
```

Requirements:

- Serialization occurs at enqueue time from the parsed method-call expression.
- Deserialization occurs immediately before invocation.
- The serializer owns only the method argument array. It does not own service type, method identity, priority, state, or concurrency metadata.
- The serializer must preserve enough type information to rebind method arguments unambiguously.
- The serializer boundary must be used by the in-memory store as well as future durable stores.
- `SystemTextJsonJobPayloadSerializer` is the default serializer.

The in-memory provider must not bypass the serializer by storing raw delegate closures or live object instances.

## In-Memory Provider

V1 must ship with an in-memory provider used as the proof of concept for the storage abstraction. The provider lives in a separate package/project, assumed to be `Sheddueller.InMemory`.

```csharp
public static class ShedduellerInMemoryBuilderExtensions
{
    public static ShedduellerBuilder UseInMemoryStore(
        this ShedduellerBuilder builder);
}
```

Requirements:

- `UseInMemoryStore` registers the in-memory `IJobStore`.
- It must implement the same `IJobStore` contract later providers will implement.
- It must enforce the same atomic claim and concurrency-group rules as future durable providers.
- It is acceptable for its data to be process-local and non-durable.
- It is the reference implementation for unit and integration tests in v1.

## Acceptance Scenarios

The v1 implementation is complete only when the following scenarios pass:

1. Enqueueing `(service, cancellationToken) => service.DoWorkAsync(arg, cancellationToken)` persists the correct service identity, method identity, and serialized argument payload without serializing the cancellation token.
2. Unsupported expression forms are rejected during enqueue.
3. Between two jobs with the same priority, the earlier `EnqueueSequence` is claimed first.
4. A higher-priority job is claimed before any lower-priority job that would otherwise be eligible.
5. When the highest-priority queued job is blocked by group limits, the scheduler continues scanning and may claim a lower-priority job that is eligible.
6. Two nodes competing for the same queued job cannot both claim it.
7. A group limit configured on one node is enforced cluster-wide across other nodes sharing the same store.
8. A job with multiple group keys is claimable only when every referenced group has spare capacity.
9. A job referencing an unseen group key is throttled as if that group had limit `1`.
10. Lowering a group limit below current occupancy does not cancel running work, but it blocks future claims for that group until occupancy drops.
11. Successful execution moves the job to `Completed` and releases all held group occupancy.
12. A thrown exception moves the job to `Failed`, stores failure details, and does not retry.
13. A job claimed by a node that dies remains stuck in `Claimed`; the behavior is documented and tested as a v1 milestone constraint.
14. `AddSheddueller` registers the core runtime services, hosted worker, enqueuer, group manager, default JSON serializer, and configured store provider.
15. Startup validation fails deterministically when no `IJobStore` provider is registered.
16. `UseInMemoryStore` from `Sheddueller.InMemory` wires the in-memory store as the active provider.
17. `ShedduellerBuilder` composes option configuration and serializer configuration.
18. The default `SystemTextJsonJobPayloadSerializer` serializes/deserializes argument arrays through the serializer boundary.
19. A custom `IJobPayloadSerializer` can replace the default serializer.
20. Argument subexpressions are evaluated exactly once at enqueue time.
21. Generic target methods, static methods, missing cancellation-token forwarding, and captured runtime-only values are rejected during enqueue.
22. `IJobStore.TryClaimNextAsync` atomically claims the first claimable job and reserves all referenced concurrency groups.
23. The scheduler does not maintain cluster-wide concurrency-group occupancy in memory.
24. The hosted worker wakes after enqueue and after concurrency-limit updates.
25. The hosted worker still claims available work after a missed wake signal through fallback polling.
26. Each job execution resolves `TService` from a fresh `IServiceScope`.
27. `TimeProvider` controls enqueue, claim, completion, and failure timestamps in tests.
28. Host-shutdown-observed cancellation records the job as `Failed` with cancellation details.
