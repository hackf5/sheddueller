# Sheddueller v2 Specification

Status: Accepted for implementation  
Last updated: 2026-04-19

## Relationship To V1

This document extends [v1](v1-spec.md). V2 keeps the v1 priority, concurrency, clustering, and cancellation-aware task submission model, then adds resilience and scheduling.

## Summary

Sheddueller v2 adds resilience and scheduling to the v1 core.

V2 is defined by six new capabilities:

- Delayed one-shot tasks.
- Recurring schedules.
- Retry policies with fixed or exponential backoff.
- Lease and heartbeat based recovery of abandoned claimed work.
- Pending-task cancellation.
- Cooperative handler cancellation for graceful shutdown and lost ownership.

V2 continues to target `net10.0`, any number of homogeneous nodes, and backend-agnostic storage.

## Goals

- Extend v1's cancellation-aware execution model to delayed, retried, recovered, and recurring-materialized work.
- Support delayed one-shot execution through the same enqueue pipeline used for immediate work.
- Support recurring schedules that materialize ordinary task instances.
- Recover abandoned work after node failure.
- Provide at-least-once execution through retries and lease-based reclaim.
- Keep schedule ownership cluster-safe without introducing a leader node.

## Non-Goals

Non-goals are scoped to v2 unless explicitly marked permanent.

- Exactly-once execution.
- Time-zone aware or local-time cron evaluation.
- Second-level cron precision.
- Bulk backfill of missed recurring fires.
- User-initiated cancellation of already claimed work beyond scheduler-owned shutdown and lease-loss signaling.
- Result retrieval APIs.

## Architecture Additions

- Nodes remain homogeneous. Any node may enqueue work, claim work, renew leases, recover expired claims, and materialize recurring occurrences.
- Delayed tasks, retries, and recurring occurrences all produce ordinary task records in the shared store.
- Recurring schedule materialization must be coordinated through the store. A due occurrence may be materialized by any node, but never more than once.
- Each executing handler receives a scheduler-owned `CancellationToken` linked to host shutdown and local claim ownership.
- At-least-once execution is an explicit contract. Handlers must be safe for duplicate execution or implement application-level deduplication, and they must honor the provided cancellation token.

## Public API Shape

The API names in this section are normative. Additional overloads are allowed, but these primary entry points and types define the v2 surface.

### Registration

```csharp
public sealed class ShedduellerOptions
{
    public string? NodeId { get; set; }
    public int MaxConcurrentExecutionsPerNode { get; set; } = Environment.ProcessorCount;
    public TimeSpan IdlePollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public RetryPolicy? DefaultRetryPolicy { get; set; }
}
```

Requirements:

- `IdlePollingInterval` remains the v1 fallback poll cadence and is also used for expired-lease recovery scans and recurring schedule materialization checks.
- `IdlePollingInterval` must be positive.
- `LeaseDuration` must be positive.
- `HeartbeatInterval` must be positive and strictly less than `LeaseDuration`.
- `DefaultRetryPolicy` is optional. If it is `null`, tasks and schedules do not retry unless they provide their own retry policy.

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
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    DateTimeOffset? NotBeforeUtc = null,
    RetryPolicy? RetryPolicy = null);
```

Requirements:

- Submitted work continues to accept the scheduler-provided `CancellationToken` defined in v1.
- `NotBeforeUtc` is optional. If omitted or in the past, the task is immediately eligible for claim.
- `NotBeforeUtc` must be normalized to UTC before persistence.
- Per-task `RetryPolicy` overrides `ShedduellerOptions.DefaultRetryPolicy`.
- The expression must forward its lambda `CancellationToken` parameter to the target service method call.
- The scheduler-provided `CancellationToken` is runtime-owned and is never serialized as part of task payload.
- Callers must not capture an external `CancellationToken` for execution control inside the submitted expression.
- The v1 expression restrictions still apply to delayed and retried tasks.

### Retry Policy

```csharp
public sealed record RetryPolicy(
    int MaxAttempts,
    RetryBackoffKind BackoffKind,
    TimeSpan BaseDelay,
    TimeSpan? MaxDelay = null);

public enum RetryBackoffKind
{
    Fixed,
    Exponential
}
```

Requirements:

- `MaxAttempts` counts total execution attempts, including the first claim. It must be greater than or equal to `1`.
- A safely released scheduler-owned interruption is not counted as an execution attempt for retry-budget purposes.
- `BaseDelay` must be positive.
- `MaxDelay`, when provided, must be greater than or equal to `BaseDelay`.
- Backoff is computed after a failed or abandoned attempt `n`:
  - `Fixed`: `BaseDelay`
  - `Exponential`: `min(BaseDelay * 2^(n - 1), MaxDelay ?? infinity)`
- No retries means either no policy is present or the effective policy has `MaxAttempts = 1`.

### Pending Task Management

```csharp
public interface ITaskManager
{
    ValueTask<bool> CancelAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);
}
```

Requirements:

- `CancelAsync` succeeds only when the task is still unclaimed.
- If cancellation wins the race, the task transitions to `Canceled` and is never claimed.
- If claim or a terminal transition wins first, `CancelAsync` returns `false`.

### Recurring Schedules

```csharp
public interface IRecurringScheduleManager
{
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    ValueTask<bool> PauseAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ResumeAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    ValueTask<RecurringScheduleInfo?> GetAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<RecurringScheduleInfo> ListAsync(
        CancellationToken cancellationToken = default);
}

public enum RecurringScheduleUpsertResult
{
    Created,
    Updated,
    Unchanged
}

public sealed record RecurringScheduleOptions(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    RetryPolicy? RetryPolicy = null,
    RecurringOverlapMode OverlapMode = RecurringOverlapMode.Skip);

public enum RecurringOverlapMode
{
    Skip,
    Allow
}

public sealed record RecurringScheduleInfo(
    string ScheduleKey,
    string CronExpression,
    bool IsPaused,
    RecurringOverlapMode OverlapMode,
    int Priority,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    RetryPolicy? RetryPolicy,
    DateTimeOffset? NextFireAtUtc);
```

Requirements:

- `scheduleKey` is caller-supplied, unique, case-sensitive, and non-empty.
- Recurring schedule expressions follow the same cancellation-token requirements as `ITaskEnqueuer`.
- `CreateOrUpdateAsync` is atomic by `scheduleKey`.
- `CreateOrUpdateAsync` returns `Created` when it inserts a new schedule.
- `CreateOrUpdateAsync` returns `Updated` when an existing schedule's cron expression, target work, or options differ and are atomically overwritten by the incoming definition.
- `CreateOrUpdateAsync` returns `Unchanged` when the existing schedule already matches the incoming cron expression, target work, and options.
- `CreateOrUpdateAsync` does not merge definitions. The incoming cron expression, target work, and options replace the existing stored definition as a whole.
- A new schedule created by `CreateOrUpdateAsync` starts active.
- `CreateOrUpdateAsync` does not implicitly pause or resume a schedule. Existing pause state is preserved across updates and exact-idempotent reapplication.
- `CreateOrUpdateAsync` does not cancel already materialized tasks.
- `DeleteAsync`, `PauseAsync`, and `ResumeAsync` are idempotent and return `false` only when the schedule does not exist.
- `DeleteAsync` prevents future occurrences from being materialized, but it does not cancel already materialized tasks.

## Task Model Changes

The v1 task record is extended with at least the following fields:

| Field | Type | Notes |
| --- | --- | --- |
| `NotBeforeUtc` | `DateTimeOffset?` | Earliest claim time for delayed and retried tasks. |
| `AttemptCount` | `int` | Number of retry-budget attempts consumed by successful claims, excluding scheduler-owned interruptions that were safely released. |
| `MaxAttempts` | `int` | Effective total attempt limit for this task. |
| `RetryBackoffKind` | `RetryBackoffKind?` | Present only when retries are enabled. |
| `RetryBaseDelay` | `TimeSpan?` | Present only when retries are enabled. |
| `RetryMaxDelay` | `TimeSpan?` | Present only when retries are enabled. |
| `LeaseToken` | `Guid?` | Store-generated ownership token for the current claim. |
| `LeaseExpiresAtUtc` | `DateTimeOffset?` | Current lease expiry for the active claim. |
| `LastHeartbeatAtUtc` | `DateTimeOffset?` | Last successful heartbeat for the active claim. |
| `CanceledAtUtc` | `DateTimeOffset?` | Present when a pending task is canceled. |
| `SourceScheduleKey` | `string?` | Set for tasks materialized from recurring schedules. |
| `ScheduledFireAtUtc` | `DateTimeOffset?` | Cron occurrence time that produced the task instance. |

`TaskState` in v2 contains:

- `Queued`
- `Claimed`
- `Completed`
- `Failed`
- `Canceled`

Requirements:

- Delayed tasks and retry-wait tasks remain in `Queued`. Eligibility is controlled by `NotBeforeUtc`.
- `AttemptCount` increments when a claim succeeds.
- A scheduler-owned interruption that is safely released rolls `AttemptCount` back to the value before the interrupted claim.
- `LeaseToken` changes on every successful claim and reclaim.
- Terminal transitions, heartbeats, and scheduler-owned interruption releases must validate the current `LeaseToken`. Stale owners must not be allowed to complete, fail, renew, or release a reclaimed task.

## Scheduling And Eligibility

### Immediate And Delayed Tasks

- A task is claimable only when:
  - `State` is `Queued`
  - `NotBeforeUtc` is `null` or less than or equal to the current UTC time
  - all referenced concurrency groups have free capacity
- Delayed tasks are created by setting `NotBeforeUtc` in the future.
- The v1 ordering rule remains in force for claimable tasks: `Priority DESC, EnqueueSequence ASC`.

### Pending Cancellation

- Only `Queued` tasks may be canceled.
- A canceled task never reaches `Claimed`.
- Canceling a recurring schedule occurrence affects only that task instance. It does not modify the recurring schedule definition.

## Retry Semantics

- The effective retry policy for a task is:
  - the per-task or per-schedule policy, when present
  - otherwise `ShedduellerOptions.DefaultRetryPolicy`, when present
  - otherwise no retries
- A failed execution or expired lease consumes the already-issued attempt.
- Scheduler-owned interruption is not a failed execution and does not consume retry budget when the owner safely releases the task before losing the current lease.
- If `AttemptCount` is still less than `MaxAttempts` after failure processing, the task returns to `Queued` with a new `NotBeforeUtc` based on its retry backoff.
- If `AttemptCount` is equal to `MaxAttempts` after failure processing, the task transitions to `Failed`.
- Retry state is per materialized task instance. Recurring schedules do not share retry budget across occurrences.

## Lease, Heartbeat, And Recovery

### Claiming

- Claiming a task must atomically:
  - transition it from `Queued` to `Claimed`
  - increment `AttemptCount`
  - assign a new `LeaseToken`
  - set `LeaseExpiresAtUtc` to `now + LeaseDuration`
  - reserve all concurrency-group capacity for the task

### Heartbeats

- A node that owns a claimed task must renew its lease before `LeaseExpiresAtUtc`.
- Heartbeat renewal must succeed only when the caller presents the current `LeaseToken`.
- Successful renewal sets `LastHeartbeatAtUtc` to the current time and extends `LeaseExpiresAtUtc` by `LeaseDuration`.

### Execution Cancellation

- The worker must invoke the target service method with a scheduler-owned execution token.
- That execution token must be linked to:
  - host shutdown
  - loss of local task ownership, including failed lease renewal or confirmed reclaim by another node
- When host shutdown begins, the node must stop claiming new tasks and cancel all in-flight execution tokens.
- When local ownership is lost, the node must cancel the local execution token even if user code is still running.
- Cooperative cancellation does not weaken the at-least-once contract. If the process exits before it records a safe state transition, normal lease-expiry recovery still applies.
- If user code responds to the scheduler-owned token by throwing `OperationCanceledException` or returning a canceled task, Sheddueller must treat that as scheduler-requested interruption rather than a business exception.
- A scheduler-requested interruption must attempt a lease-token-protected release that returns the task to `Queued`, clears current ownership fields, releases concurrency-group occupancy, removes any retry backoff delay caused by the interrupted attempt, and restores `AttemptCount` to the value before the interrupted claim.
- If the release loses a race with lease expiry or reclaim, the store must reject it as a stale-owner transition and normal recovery applies.
- If a handler returns successfully after the scheduler-owned token has been canceled, Sheddueller records normal completion as long as the lease token is still current.

### Lease Expiry

- When `LeaseExpiresAtUtc` passes without successful renewal, the claim is abandoned.
- An abandoned claim is processed as a failed attempt.
- If retries remain, the task transitions back to `Queued`, releases its concurrency-group occupancy, receives a new retry `NotBeforeUtc`, and becomes eligible for reclaim later.
- If no retries remain, the task transitions to terminal `Failed` and releases its concurrency-group occupancy.

### Stale Owners

- A node that loses its lease may continue running the handler briefly. This is allowed by the at-least-once contract.
- If that stale node later attempts to complete, fail, heartbeat, or release the task with an old `LeaseToken`, the store must reject the transition.
- Rejection of stale-owner transitions prevents old owners from corrupting the state of a reclaimed task. It does not prevent duplicate side effects outside Sheddueller.

## Recurring Schedules

### Cron Rules

- V2 supports standard 5-field cron expressions with minute precision only.
- Cron expressions are evaluated in UTC only.
- V2 uses `Cronos` for cron parsing and occurrence calculation.
- The first `NextFireAtUtc` for a new schedule is the first cron occurrence strictly after the creation time.

### Materialization

- Recurring schedules do not invoke handlers directly.
- Each due occurrence materializes exactly one ordinary task record with:
  - inherited priority
  - inherited concurrency-group keys
  - inherited retry policy
  - `SourceScheduleKey`
  - `ScheduledFireAtUtc`
- Materializing a due occurrence must atomically:
  - verify the schedule is still active and due
  - apply its overlap policy
  - enqueue the occurrence task when allowed
  - advance `NextFireAtUtc` to the next future cron occurrence

### Overlap

- `Allow` means every due occurrence may materialize a new task.
- `Skip` means the current due occurrence is dropped when any non-terminal task from the same `SourceScheduleKey` already exists.
- For overlap checks, non-terminal means `Queued` or `Claimed`.
- When `Skip` suppresses an occurrence, the scheduler still advances `NextFireAtUtc`. The skipped fire is not retried later.

### Pause, Resume, Upsert, And Delete

- A paused schedule materializes no new tasks.
- `ResumeAsync` recomputes `NextFireAtUtc` as the first cron occurrence strictly after the resume time.
- `CreateOrUpdateAsync` with result `Updated` recomputes `NextFireAtUtc` as the first cron occurrence strictly after the successful upsert time when the schedule is active.
- `CreateOrUpdateAsync` with result `Unchanged` leaves `NextFireAtUtc` unchanged.
- `CreateOrUpdateAsync` on a paused schedule updates the stored definition but does not resume the schedule; `ResumeAsync` remains responsible for recomputing `NextFireAtUtc`.
- If a schedule is overdue because no node materialized it on time, v2 materializes at most the stored overdue occurrence once, then advances `NextFireAtUtc` to the next future cron occurrence.
- Pause/resume, upsert, downtime, or prolonged blockage never bulk-backfills missed occurrences.
- Deleting a schedule removes the definition and stops future materialization only.

### Failure Behavior

- Failure of one recurring occurrence does not pause, disable, or delete the recurring schedule.
- Later occurrences continue to materialize according to the schedule definition.

## Storage Abstraction Changes

`ITaskStore` remains a first-class extension point. V2 requires the abstraction to add support for:

- delayed-task eligibility based on `NotBeforeUtc`
- retry metadata and retry requeue transitions
- atomic claims with lease creation and `LeaseToken` issuance
- heartbeat renewal by `LeaseToken`
- stale-owner rejection for terminal transitions
- lease-token-protected release of scheduler-owned interruptions without retry-budget consumption
- expired-lease recovery
- pending-task cancellation
- atomic recurring schedule upsert and lifecycle management
- atomic overlap checks for `RecurringOverlapMode.Skip`

Required behavioral rules:

- Claim selection, attempt increment, lease issuance, and concurrency-group reservation must be one atomic store operation.
- Scheduler-owned interruption release must validate the current `LeaseToken` and release concurrency-group occupancy exactly once.
- Lease expiry recovery must release concurrency-group occupancy exactly once.
- A recurring occurrence must never be materialized more than once for the same schedule and fire time.
- Schedule materialization and advancement of `NextFireAtUtc` must be one atomic store operation.
- The store abstraction must remain backend-agnostic and must not introduce provider-specific APIs.

## Acceptance Scenarios

The v2 implementation is complete only when the following scenarios pass:

1. A task enqueued with future `NotBeforeUtc` remains unclaimable until that time and becomes claimable afterward.
2. A task with no effective retry policy fails terminally after its first failed attempt.
3. A task with a retry policy is requeued with the correct `NotBeforeUtc` after a failed attempt, using fixed or exponential backoff as configured.
4. `AttemptCount` increments on claim, not on terminal transition.
5. Scheduler-owned interruption requeues the task without consuming retry budget when the current lease owner safely releases it.
6. Host shutdown cancellation does not terminally fail a no-retry task when the handler cooperatively exits by cancellation and the release succeeds.
7. A healthy node renews leases successfully and prevents reclaim while heartbeats continue.
8. If a node stops heartbeating, another node eventually recovers the task after lease expiry.
9. Lease expiry consumes the current attempt and either requeues or fails the task based on remaining attempts.
10. A stale owner cannot complete, fail, heartbeat, or release a task after another node has reclaimed it.
11. `CancelAsync` succeeds for a queued task and fails once a claim has already won the race.
12. `CreateOrUpdateAsync` returns `Created` for a new schedule, `Updated` when the incoming definition changes an existing schedule, and `Unchanged` for an exact-idempotent reapplication.
13. `CreateOrUpdateAsync` preserves pause state and does not cancel already materialized tasks.
14. A recurring schedule with a due fire materializes exactly one ordinary task instance across the cluster.
15. An overdue recurring schedule materializes at most one catch-up occurrence, then advances to the next future UTC occurrence.
16. A paused schedule does not materialize new occurrences until resumed.
17. `ResumeAsync` and `CreateOrUpdateAsync` with result `Updated` use the next future UTC cron occurrence only and do not bulk-backfill missed fires.
18. `RecurringOverlapMode.Skip` drops an occurrence when an earlier occurrence from the same schedule is still non-terminal.
19. `RecurringOverlapMode.Allow` permits multiple non-terminal occurrences from the same schedule.
20. A recurring occurrence inherits priority, concurrency groups, and retry policy from the schedule definition.
21. Deleting a recurring schedule stops future occurrences but does not cancel already materialized tasks.
22. A failed recurring occurrence does not disable the recurring schedule.
23. Enqueue and recurring-schedule registration reject expressions that do not use the scheduler-provided `CancellationToken`.
24. When host shutdown begins, in-flight handlers receive a canceled execution token and the node stops claiming new tasks.
25. When a node loses local ownership of a claimed task, the local execution token is canceled.
