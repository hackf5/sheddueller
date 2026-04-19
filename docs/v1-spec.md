# Sheddueller v1 Specification

Status: Accepted for implementation  
Last updated: 2026-04-18

## Summary

Sheddueller v1 is an in-process task scheduler for `net10.0`. It executes immediate, fire-and-forget tasks across any number of homogeneous application hosts that share one logical task store.

V1 is defined by four core capabilities:

- Strict numeric task priority.
- Dynamic cluster-wide concurrency groups.
- Expression-based task submission against DI services.
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
- The worker claims tasks from the store, resolves the target service from DI, invokes the captured method, and reports terminal completion back to the store.
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
}
```

Requirements:

- `AddSheddueller` must register the scheduler services, the hosted worker, and the default app-facing interfaces.
- `NodeId` is optional. If not configured, Sheddueller must generate a unique process-instance identifier at startup.
- `MaxConcurrentExecutionsPerNode` must be a positive integer. Its default is `Environment.ProcessorCount`.

### Task Submission

```csharp
public interface ITaskEnqueuer
{
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, Task>> work,
        TaskSubmission? submission = null,
        CancellationToken cancellationToken = default);

    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, ValueTask>> work,
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
- Method arguments are captured at enqueue time and serialized through `ITaskPayloadSerializer`.
- Captured argument values must be serializer-compatible.
- `TaskSubmission.Priority` is an unconstrained sortable integer. Higher values mean higher priority.
- `TaskSubmission.ConcurrencyGroupKeys` is optional. Missing or empty means the task has no concurrency-group constraints.
- Group keys are opaque, case-sensitive, non-empty strings.
- Duplicate group keys in a submission must be deduplicated before persistence.

Unsupported expression forms:

- Static method calls.
- Property access without a terminal method call.
- Lambdas containing control flow, loops, or multiple method calls.
- Captures of live service instances, delegates, streams, or other unserializable runtime-only objects.

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
- The worker invokes the captured method using the deserialized arguments.
- On successful completion, the task transitions to `Completed`.
- On exception, the task transitions to `Failed`.
- V1 does not retry failed tasks.

### Node Failure

- If a node dies after claiming a task, the task remains in `Claimed`.
- V1 does not include lease expiration, heartbeat renewal, or automatic reclaim.
- This is an intentional limitation, not an implementation gap.

## Storage Abstraction

`ITaskStore` is a first-class extension point. V1 ships with an in-memory implementation, but the abstraction must be suitable for later relational providers.

The store contract must provide these capabilities:

- Enqueue a task and assign its `EnqueueSequence`.
- Return the next claimable task according to the scheduling rules.
- Perform claim selection atomically with the transition to `Claimed`.
- Persist terminal transitions to `Completed` and `Failed`.
- Store and retrieve configured concurrency-group limits.
- Read enough task state to enforce group occupancy across the cluster.

Required behavioral rules:

- Claim selection and group-capacity reservation must be one atomic store operation.
- Group occupancy is the number of tasks in `Claimed` that reference a given group key.
- Terminal transitions must release all group occupancy held by that task.
- The store abstraction must not encode PostgreSQL-, MySQL-, or provider-specific concepts into app-facing APIs.

The spec does not lock v1 to a particular SQL schema. It locks the behavior and the storage responsibilities.

## Payload Serialization

`ITaskPayloadSerializer` is a first-class extension point.

Requirements:

- Serialization occurs at enqueue time from the parsed method-call expression.
- Deserialization occurs immediately before invocation.
- The serializer must preserve enough type information to rebind method arguments unambiguously.
- The serializer boundary must be used by the in-memory store as well as future durable stores.

The in-memory provider must not bypass the serializer by storing raw delegate closures or live object instances.

## In-Memory Provider

V1 must ship with an in-memory provider used as the proof of concept for the storage abstraction.

Requirements:

- It must implement the same `ITaskStore` contract later providers will implement.
- It must enforce the same atomic claim and concurrency-group rules as future durable providers.
- It is acceptable for its data to be process-local and non-durable.
- It is the reference implementation for unit and integration tests in v1.

## Acceptance Scenarios

The v1 implementation is complete only when the following scenarios pass:

1. Enqueueing `service => service.DoWork(arg)` persists the correct service identity, method identity, and serialized argument payload.
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
13. A task claimed by a node that dies remains stuck in `Claimed`; the behavior is documented and tested as a v1 limitation.

## Known Limitations

- V1 intentionally has no delayed scheduling.
- V1 intentionally has no recurring jobs.
- V1 intentionally has no task dependency model.
- V1 intentionally has no retries.
- V1 intentionally has no result retrieval API.
- V1 intentionally has no automatic recovery for dead-node claims.
- V1 intentionally keeps observability minimal; richer inspection APIs can be added in a later version without changing the core scheduling model.
