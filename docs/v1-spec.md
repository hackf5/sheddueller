# Sheddueller v1 Specification

Status: Accepted for implementation  
Last updated: 2026-04-19

## Summary

Sheddueller v1 is an in-process task scheduler for `net10.0`. It executes immediate, fire-and-forget tasks across any number of homogeneous application hosts that share one logical task store.

V1 is defined by four core capabilities:

- Strict numeric task priority.
- Dynamic cluster-wide concurrency groups.
- Cancellation-aware expression-based task submission against DI services.
- Backend-agnostic storage, proven first by an in-memory provider.

Hosting integration is built around the standard .NET host model:

- `HostApplicationBuilder`: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostapplicationbuilder?view=net-10.0-pp>
- `IHostedService`: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.ihostedservice?view=net-10.0-pp>

## Goals

- Execute immediate tasks submitted by application code.
- Allow callers to assign any integer priority at enqueue time.
- Enforce concurrency limits with runtime-defined group keys instead of preconfigured queues.
- Allow any number of hosts to join the same cluster and compete for work from the shared store.
- Keep the storage contract independent from any specific relational engine.
- Prove the storage contract with an in-memory provider before adding SQL-backed providers.

## Non-Goals

Non-goals are scoped to v1 unless explicitly marked permanent.

- Delayed or scheduled execution.
- Recurring or cron-based work.
- Task chaining, workflows, or dependency graphs.
- Automatic retries or configurable retry policies.
- Result storage or return-value retrieval.
- First-class observability or operator dashboards.
- Automatic recovery of work claimed by a dead node.

## Architecture

- Every application host is a homogeneous node. There is no leader role in v1.
- Each node joins the cluster by registering Sheddueller in DI and running a hosted background worker.
- All nodes share one logical `ITaskStore`.
- Application code enqueues work through `ITaskEnqueuer`.
- The worker claims tasks from the store, resolves the target service from DI, invokes the captured method with a scheduler-owned `CancellationToken`, and reports terminal completion back to the store.
- Nodes execute up to `MaxConcurrentExecutionsPerNode` tasks concurrently. This node-local limit is separate from cluster-wide concurrency groups.

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
- Runtime-generated timestamps passed to `ITaskStore` must come from the registered `TimeProvider`.

### Builder

```csharp
public sealed class ShedduellerBuilder
{
    public IServiceCollection Services { get; }

    public ShedduellerBuilder ConfigureOptions(
        Action<ShedduellerOptions> configure);

    public ShedduellerBuilder UseTaskPayloadSerializer<TSerializer>()
        where TSerializer : class, ITaskPayloadSerializer;

    public ShedduellerBuilder UseTaskPayloadSerializer(
        ITaskPayloadSerializer serializer);
}
```

Requirements:

- `ShedduellerBuilder` is the fluent configuration surface used by `AddSheddueller`.
- `Services` exposes the underlying service collection for provider packages.
- `ConfigureOptions` composes with other option configuration.
- `UseTaskPayloadSerializer<TSerializer>` registers a singleton serializer implementation.
- `UseTaskPayloadSerializer(ITaskPayloadSerializer)` registers the provided serializer instance as singleton.
- If no serializer is configured, Sheddueller must use the built-in `SystemTextJsonTaskPayloadSerializer`.
- Store providers must extend `ShedduellerBuilder` from their own package. V1's in-memory provider exposes `UseInMemoryStore`.
- `AddSheddueller` must fail during startup validation if no `ITaskStore` provider has been registered.
- Startup validation should run before hosted workers begin claiming work.

### Task Submission

```csharp
public interface ITaskEnqueuer
{
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, Task>> work,
        TaskSubmission? submission = null,
        CancellationToken cancellationToken = default);

    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        TaskSubmission? submission = null,
        CancellationToken cancellationToken = default);
}

public sealed record TaskSubmission(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null);
```

Requirements:

- The expression must be a single instance-method call on `TService`.
- `TService` must be resolvable from DI at execution time.
- Supported task methods return `Task` or `ValueTask`.
- The expression must accept the scheduler-provided `CancellationToken` and forward it to the target service method call.
- The scheduler-provided `CancellationToken` is runtime-owned and is never serialized as part of the task payload.
- Callers must not capture an external `CancellationToken` for execution control inside the submitted expression.
- V1 supports non-generic instance method calls only.
- V1 does not support open or closed generic target method calls.
- Optional/default parameter behavior is not inferred. Every target method argument except the scheduler-owned cancellation token must appear explicitly in the method call expression.
- Argument subexpressions are evaluated once at enqueue time and the resulting values are serialized.
- Method arguments are captured at enqueue time and serialized through `ITaskPayloadSerializer`.
- Captured argument values must be serializer-compatible.
- `TaskSubmission.Priority` is an unconstrained sortable integer. Higher values mean higher priority.
- `TaskSubmission.ConcurrencyGroupKeys` is optional. Missing or empty means the task has no concurrency-group constraints.
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
- Lowering a limit below the current number of claimed tasks must not preempt running work. It only blocks future claims until occupancy drops below the new limit.

## Task Model

The persisted task record must contain at least the following fields:

| Field | Type | Notes |
| --- | --- | --- |
| `TaskId` | `Guid` | Stable identifier returned from `EnqueueAsync`. |
| `State` | `TaskState` | One of `Queued`, `Claimed`, `Completed`, `Failed`. |
| `Priority` | `int` | Higher numbers run first. |
| `EnqueueSequence` | `long` | Store-assigned, strictly monotonic ordering key used for FIFO within equal priority. |
| `EnqueuedAtUtc` | `DateTimeOffset` | Timestamp recorded at enqueue time. |
| `ServiceType` | `string` | Assembly-qualified or otherwise unambiguous service type identity. |
| `MethodName` | `string` | Target method name. |
| `MethodParameterTypes` | `string[]` | Unambiguous parameter type identities for overload resolution. |
| `SerializedArguments` | `SerializedTaskPayload` | Serializer-owned payload for method arguments. |
| `ConcurrencyGroupKeys` | `string[]` | Distinct persisted group keys for this task. |
| `ClaimedByNodeId` | `string?` | Node that claimed the task, if any. |
| `ClaimedAtUtc` | `DateTimeOffset?` | When the claim occurred. |
| `CompletedAtUtc` | `DateTimeOffset?` | When successful execution finished. |
| `FailedAtUtc` | `DateTimeOffset?` | When failed execution finished. |
| `Failure` | `TaskFailureInfo?` | Best-effort error details for failed tasks. |

`TaskFailureInfo` must contain:

- Exception type name.
- Exception message.
- Best-effort stack trace text.

`TaskState` has the following meaning:

- `Queued`: available to be considered for claiming.
- `Claimed`: owned by a node and counted against every referenced concurrency group.
- `Completed`: terminal success state.
- `Failed`: terminal failure state.

There is no separate `Running` state in v1. A claimed task is considered active until it transitions to `Completed` or `Failed`.

## Scheduling Semantics

### Ordering

- The global candidate order is `Priority DESC, EnqueueSequence ASC`.
- FIFO is defined by `EnqueueSequence`, not by wall-clock timestamps.
- The store must assign `EnqueueSequence` atomically at enqueue time.

### Claiming

- A task may be claimed only from `Queued`.
- Claiming a task must atomically transition it to `Claimed` and reserve capacity in every referenced concurrency group.
- Two nodes must never successfully claim the same task.
- Two nodes must never oversubscribe the same concurrency group beyond its effective limit.

### Concurrency Groups

- Concurrency groups are cluster-wide across all nodes sharing the same store.
- The effective limit for a group is:
  - the configured limit from `IConcurrencyGroupManager`, when present
  - otherwise `1`
- A task with multiple group keys may start only when every group has available capacity.
- Tasks without group keys are not subject to group-based throttling.

### Scanning Behavior

- The scheduler must not stop at the first queued task if that task is blocked by concurrency limits.
- The scheduler must continue scanning queued tasks in global candidate order until it finds the first claimable task.
- This rule exists so a blocked high-priority task does not stall unrelated work indefinitely.

### Execution

- After claiming, the node resolves the target `TService` from DI.
- Each claimed task executes in a fresh `IServiceScope`.
- The worker invokes the captured method using the deserialized arguments and a scheduler-owned execution `CancellationToken`.
- On successful completion, the task transitions to `Completed`.
- On exception, the task transitions to `Failed`.
- If host shutdown cancels the execution token and user code observes it by throwing `OperationCanceledException` or returning a canceled task, v1 transitions the task to `Failed` with cancellation details because v1 has no `Canceled` state.
- V1 does not retry failed tasks.

### Worker Loop

- The hosted worker must stop claiming new tasks when host shutdown begins.
- The hosted worker must use signal-plus-poll waiting.
- Enqueueing a task must signal workers that work may be available.
- Updating a concurrency-group limit must signal workers that blocked work may now be available.
- If a signal is missed, the worker must still make progress through fallback polling using `IdlePollingInterval`.
- The wake signal is a core runtime service. It is not part of `ITaskStore`.
- The worker must never execute more than `MaxConcurrentExecutionsPerNode` tasks concurrently.

### Node Failure

- If a node dies after claiming a task, the task remains in `Claimed`.
- V1 does not include lease expiration, heartbeat renewal, or automatic reclaim.
- This is an intentional limitation, not an implementation gap.

## Storage Abstraction

`ITaskStore` is a first-class extension point. V1 ships with an in-memory implementation in a separate provider package, but the abstraction must be suitable for later relational providers.

```csharp
public interface ITaskStore
{
    ValueTask<EnqueueTaskResult> EnqueueAsync(
        EnqueueTaskRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ClaimTaskResult> TryClaimNextAsync(
        ClaimTaskRequest request,
        CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(
        FailTaskRequest request,
        CancellationToken cancellationToken = default);

    ValueTask SetConcurrencyLimitAsync(
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}

public sealed record EnqueueTaskRequest(
    Guid TaskId,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedTaskPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    DateTimeOffset EnqueuedAtUtc);

public sealed record EnqueueTaskResult(
    Guid TaskId,
    long EnqueueSequence);

public sealed record ClaimTaskRequest(
    string NodeId,
    DateTimeOffset ClaimedAtUtc);

public abstract record ClaimTaskResult
{
    public sealed record Claimed(ClaimedTask Task) : ClaimTaskResult;
    public sealed record NoTaskAvailable() : ClaimTaskResult;
}

public sealed record ClaimedTask(
    Guid TaskId,
    long EnqueueSequence,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedTaskPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys);

public sealed record CompleteTaskRequest(
    Guid TaskId,
    string NodeId,
    DateTimeOffset CompletedAtUtc);

public sealed record FailTaskRequest(
    Guid TaskId,
    string NodeId,
    DateTimeOffset FailedAtUtc,
    TaskFailureInfo Failure);

public sealed record SetConcurrencyLimitRequest(
    string GroupKey,
    int Limit,
    DateTimeOffset UpdatedAtUtc);
```

The store contract must provide these capabilities:

- Enqueue a task and assign its `EnqueueSequence`.
- Return the next claimable task according to the scheduling rules.
- Perform claim selection atomically with the transition to `Claimed`.
- Persist terminal transitions to `Completed` and `Failed`.
- Store and retrieve configured concurrency-group limits.
- Read enough task state to enforce group occupancy across the cluster.
- The operation timestamps supplied by the runtime are authoritative for v1 stores.

Required behavioral rules:

- Claim selection and group-capacity reservation must be one atomic store operation.
- The store owns concurrency-group occupancy. The scheduler must not track cluster-wide group occupancy in memory.
- Group occupancy is the number of tasks in `Claimed` that reference a given group key.
- Terminal transitions must release all group occupancy held by that task.
- The store abstraction must not encode PostgreSQL-, MySQL-, or provider-specific concepts into app-facing APIs.

The spec does not lock v1 to a particular SQL schema. It locks the behavior and the storage responsibilities.

## Payload Serialization

`ITaskPayloadSerializer` is a first-class extension point.

```csharp
public interface ITaskPayloadSerializer
{
    ValueTask<SerializedTaskPayload> SerializeAsync(
        IReadOnlyList<object?> arguments,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<object?>> DeserializeAsync(
        SerializedTaskPayload payload,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);
}

public sealed record SerializedTaskPayload(
    string ContentType,
    byte[] Data);

public sealed class SystemTextJsonTaskPayloadSerializer : ITaskPayloadSerializer;
```

Requirements:

- Serialization occurs at enqueue time from the parsed method-call expression.
- Deserialization occurs immediately before invocation.
- The serializer owns only the method argument array. It does not own service type, method identity, priority, state, or concurrency metadata.
- The serializer must preserve enough type information to rebind method arguments unambiguously.
- The serializer boundary must be used by the in-memory store as well as future durable stores.
- `SystemTextJsonTaskPayloadSerializer` is the default serializer.

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

- `UseInMemoryStore` registers the in-memory `ITaskStore`.
- It must implement the same `ITaskStore` contract later providers will implement.
- It must enforce the same atomic claim and concurrency-group rules as future durable providers.
- It is acceptable for its data to be process-local and non-durable.
- It is the reference implementation for unit and integration tests in v1.

## Acceptance Scenarios

The v1 implementation is complete only when the following scenarios pass:

1. Enqueueing `(service, cancellationToken) => service.DoWorkAsync(arg, cancellationToken)` persists the correct service identity, method identity, and serialized argument payload without serializing the cancellation token.
2. Unsupported expression forms are rejected during enqueue.
3. Between two tasks with the same priority, the earlier `EnqueueSequence` is claimed first.
4. A higher-priority task is claimed before any lower-priority task that would otherwise be eligible.
5. When the highest-priority queued task is blocked by group limits, the scheduler continues scanning and may claim a lower-priority task that is eligible.
6. Two nodes competing for the same queued task cannot both claim it.
7. A group limit configured on one node is enforced cluster-wide across other nodes sharing the same store.
8. A task with multiple group keys is claimable only when every referenced group has spare capacity.
9. A task referencing an unseen group key is throttled as if that group had limit `1`.
10. Lowering a group limit below current occupancy does not cancel running work, but it blocks future claims for that group until occupancy drops.
11. Successful execution moves the task to `Completed` and releases all held group occupancy.
12. A thrown exception moves the task to `Failed`, stores failure details, and does not retry.
13. A task claimed by a node that dies remains stuck in `Claimed`; the behavior is documented and tested as a v1 milestone constraint.
14. `AddSheddueller` registers the core runtime services, hosted worker, enqueuer, group manager, default JSON serializer, and configured store provider.
15. Startup validation fails deterministically when no `ITaskStore` provider is registered.
16. `UseInMemoryStore` from `Sheddueller.InMemory` wires the in-memory store as the active provider.
17. `ShedduellerBuilder` composes option configuration and serializer configuration.
18. The default `SystemTextJsonTaskPayloadSerializer` serializes/deserializes argument arrays through the serializer boundary.
19. A custom `ITaskPayloadSerializer` can replace the default serializer.
20. Argument subexpressions are evaluated exactly once at enqueue time.
21. Generic target methods, static methods, missing cancellation-token forwarding, and captured runtime-only values are rejected during enqueue.
22. `ITaskStore.TryClaimNextAsync` atomically claims the first claimable task and reserves all referenced concurrency groups.
23. The scheduler does not maintain cluster-wide concurrency-group occupancy in memory.
24. The hosted worker wakes after enqueue and after concurrency-limit updates.
25. The hosted worker still claims available work after a missed wake signal through fallback polling.
26. Each task execution resolves `TService` from a fresh `IServiceScope`.
27. `TimeProvider` controls enqueue, claim, completion, and failure timestamps in tests.
28. Host-shutdown-observed cancellation records the task as `Failed` with cancellation details.
