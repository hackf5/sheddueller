# Sheddueller v5 Specification

Status: Accepted for implementation  
Last updated: 2026-04-19

## Relationship To Earlier Versions

This document extends [v1](v1-spec.md), [v2](v2-spec.md), [v3](v3-spec.md), and [v4](v4-spec.md).

V5 turns the v4 read-only jobs dashboard into a trusted operations console for small development teams. It is not a compliance product, not a multi-tenant control plane, and not an enterprise event bus. It is intended to be “Hangfire done right”: easy to self-host, easy to test, easy to understand, and useful when jobs are stuck or failing.

## Summary

Sheddueller v5 is about practical operations.

V5 is defined by five capabilities:

- Safe no-friction dashboard actions for trusted users.
- Schedule views and schedule operations.
- Concurrency group visibility.
- Worker node health.
- In-dashboard rolling metrics.

V5 keeps the dashboard embedded and host-protected as defined in v4. Everyone with dashboard access is assumed to be trusted.

## Goals

- Let trusted developers understand current scheduler state quickly.
- Let trusted developers answer what happened to a job, schedule, group, or node.
- Add practical dashboard actions for common unstick/recovery workflows.
- Keep action semantics simple and deterministic.
- Avoid audit, role-based permissions, multi-tenancy, and compliance-oriented workflow.
- Keep metrics in-app and immediately useful without requiring OpenTelemetry or external monitoring infrastructure.

## Non-Goals

Non-goals are scoped to v5 unless explicitly marked permanent.

- Audit trails with user attribution.
- Role-based authorization or permission modeling.
- Multi-tenancy or tenant isolation.
- Job payload migration or handler version migration tooling.
- Workflows, dependencies, or orchestration graphs.
- OpenTelemetry/exporter requirements.
- Concurrency group editing in v5.
- Force-killing running user code.
- Editing recurring schedule definitions from the dashboard.

## Trusted Operations Model

V5 assumes the dashboard is mounted only where trusted team members can reach it.

Requirements:

- Actions execute without permission prompts, required reason fields, or user-attribution audit records.
- Host applications remain responsible for protecting the dashboard route, as in v4.
- The dashboard records action events as job/schedule/system history so users can see what happened.
- Action events do not need to record who initiated the action.
- The UI must make action effects clear before execution, but v5 does not require confirmation prompts.

## Dashboard Action Contracts

V5 adds provider-agnostic dashboard action contracts in the dashboard package or dashboard abstractions package.

```csharp
public interface IDashboardJobActions
{
    ValueTask<RetryCloneResult> RetryFailedAsCloneAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    ValueTask<CancelJobResult> CancelJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}

public interface IDashboardScheduleActions
{
    ValueTask<ScheduleActionResult> PauseScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    ValueTask<ScheduleActionResult> ResumeScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    ValueTask<TriggerScheduleNowResult> TriggerScheduleNowAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);
}

public enum DashboardActionOutcome
{
    Succeeded,
    NotFound,
    InvalidState,
    Unsupported,
    Failed
}
```

Requirements:

- Actions are part of the dashboard/provider contract, not the core enqueueing API.
- Providers must implement actions atomically against their own storage model.
- Failed or unsupported actions must return explicit result objects instead of silently doing nothing.
- Action methods must append durable dashboard/system events that describe what happened.

## Job Actions

### Retry Failed As Clone

Retrying a failed job creates a new linked job.

```csharp
public sealed record RetryCloneResult(
    DashboardActionOutcome Outcome,
    Guid? OriginalJobId,
    Guid? NewJobId,
    string? Message = null);
```

Requirements:

- Only terminal `Failed` jobs are retry-clone candidates.
- The original failed job remains unchanged.
- The clone preserves:
  - handler service type
  - method name
  - method parameter types
  - serialized arguments
  - priority
  - concurrency group keys
  - retry policy
  - tags
- The clone receives a new job id and enqueue sequence.
- The clone is immediately queued unless normal job eligibility rules require otherwise.
- The clone stores `RetryCloneSourceJobId` pointing to the original failed job.
- The dashboard must show retry clone ancestry on both original and cloned jobs.
- Retrying a non-failed, missing, or unserializable job must return a non-success outcome and must not create a job.

### Cancel Job

V5 extends cancellation from pending-only management into dashboard-driven cooperative cancellation.

```csharp
public sealed record CancelJobResult(
    DashboardActionOutcome Outcome,
    Guid JobId,
    CancelJobResultKind Kind,
    string? Message = null);

public enum CancelJobResultKind
{
    CanceledBeforeClaim,
    CancellationRequested,
    AlreadyTerminal,
    NotFound,
    NotCancelable
}
```

Requirements:

- Queued, delayed, and retry-waiting jobs transition directly to `Canceled`.
- Claimed/running jobs receive cancellation through the scheduler-owned execution token.
- Running cancellation is cooperative. Sheddueller must not try to kill threads, processes, or service instances.
- When cancellation is requested for a running job, the job records `CancellationRequestedAtUtc`.
- If the handler observes cancellation by throwing `OperationCanceledException` or returning a canceled job from the scheduler-owned token, the job transitions to `Canceled`, records `CancellationObservedAtUtc`, and does not retry.
- If the handler ignores cancellation, the job remains claimed and may complete, fail, or expire normally.
- Canceling a terminal job returns `AlreadyTerminal`.
- Canceling a missing job returns `NotFound`.
- Runtime workers must detect cancellation requests for their claimed jobs and cancel the local scheduler-owned execution token.
- Cancellation request detection may be implemented through provider notifications, polling, or the same heartbeat loop that renews leases.

### Job Model Additions

V5 extends job metadata with:

| Field | Type | Notes |
| --- | --- | --- |
| `RetryCloneSourceJobId` | `Guid?` | Original failed job when this job was created by retry clone. |
| `CancellationRequestedAtUtc` | `DateTimeOffset?` | When a dashboard/user cancellation request was recorded. |
| `CancellationObservedAtUtc` | `DateTimeOffset?` | When cooperative cancellation was observed by the handler/runtime. |

## Schedule Views And Actions

V5 adds schedule visibility and safe schedule operations.

### Schedule Reads

```csharp
public interface IDashboardScheduleReader
{
    ValueTask<DashboardSchedulePage> SearchSchedulesAsync(
        DashboardScheduleQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DashboardScheduleDetail?> GetScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardScheduleQuery(
    string? ScheduleKey = null,
    bool? IsPaused = null,
    string? ServiceType = null,
    string? MethodName = null,
    JobTag? Tag = null,
    int PageSize = 100,
    string? ContinuationToken = null);

public sealed record DashboardSchedulePage(
    IReadOnlyList<DashboardScheduleSummary> Schedules,
    string? ContinuationToken);

public sealed record DashboardScheduleSummary(
    string ScheduleKey,
    string ServiceType,
    string MethodName,
    string CronExpression,
    bool IsPaused,
    RecurringOverlapMode OverlapMode,
    DateTimeOffset? NextFireAtUtc,
    int Priority,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    IReadOnlyList<JobTag> Tags,
    DateTimeOffset UpdatedAtUtc);

public sealed record DashboardScheduleDetail(
    DashboardScheduleSummary Summary,
    RetryPolicy? RetryPolicy,
    IReadOnlyList<DashboardScheduleOccurrence> RecentOccurrences,
    DashboardScheduleOccurrence? LastSuccessfulOccurrence,
    DashboardScheduleOccurrence? LastFailedOccurrence);

public sealed record DashboardScheduleOccurrence(
    Guid JobId,
    DateTimeOffset? ScheduledFireAtUtc,
    DashboardScheduleOccurrenceKind Kind,
    JobState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? FailedAtUtc);

public enum DashboardScheduleOccurrenceKind
{
    Automatic,
    ManualTrigger
}

public sealed record ScheduleActionResult(
    DashboardActionOutcome Outcome,
    string ScheduleKey,
    string? Message = null);
```

The schedule list/detail views show:

- schedule key
- handler service type and method name
- cron expression
- pause state
- overlap mode
- next fire time
- priority
- concurrency group keys
- tags
- retry policy
- recent occurrences
- linked materialized jobs
- last successful occurrence
- last failed occurrence
- recent trigger-now jobs

### Pause And Resume

Requirements:

- `PauseScheduleAsync` prevents future automatic materialization.
- Pausing a schedule does not cancel already materialized jobs.
- `ResumeScheduleAsync` recomputes the next future fire time using the v2 future-only semantics.
- Pause and resume append durable schedule events.

### Schedule Metadata Additions

V5 extends recurring schedule options with searchable tags.

```csharp
public sealed record RecurringScheduleOptions(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    RetryPolicy? RetryPolicy = null,
    RecurringOverlapMode OverlapMode = RecurringOverlapMode.Skip,
    IReadOnlyList<JobTag>? Tags = null);
```

Requirements:

- Schedule tags follow the same rules as v4 job tags.
- Automatically materialized schedule occurrences inherit the schedule's tags.
- Trigger-now jobs inherit the schedule's tags.

### Trigger Now

`TriggerScheduleNowAsync` creates one immediate job from the current schedule definition.

```csharp
public sealed record TriggerScheduleNowResult(
    DashboardActionOutcome Outcome,
    string ScheduleKey,
    Guid? JobId,
    string? Message = null);
```

Requirements:

- Trigger-now works even when the schedule is paused.
- Trigger-now does not change `NextFireAtUtc`.
- Trigger-now creates one ordinary job using the schedule's current handler descriptor, serialized arguments, priority, concurrency groups, retry policy, and tags.
- Trigger-now bypasses schedule overlap suppression. It is a manual fire, not a normal recurring occurrence.
- The materialized job still obeys normal job priority, due-time, retry, and concurrency group rules when claimed.
- Trigger-now jobs must record `SourceScheduleKey`.
- Trigger-now jobs must be distinguishable from normal recurring occurrences in dashboard events and schedule detail.

## Concurrency Group Views

V5 adds visibility for concurrency groups, but not editing.

```csharp
public interface IDashboardConcurrencyGroupReader
{
    ValueTask<DashboardConcurrencyGroupPage> SearchConcurrencyGroupsAsync(
        DashboardConcurrencyGroupQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DashboardConcurrencyGroupDetail?> GetConcurrencyGroupAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardConcurrencyGroupQuery(
    string? GroupKey = null,
    bool? IsSaturated = null,
    bool? HasBlockedJobs = null,
    int PageSize = 100,
    string? ContinuationToken = null);

public sealed record DashboardConcurrencyGroupPage(
    IReadOnlyList<DashboardConcurrencyGroupSummary> Groups,
    string? ContinuationToken);

public sealed record DashboardConcurrencyGroupSummary(
    string GroupKey,
    int EffectiveLimit,
    int CurrentOccupancy,
    int BlockedJobCount,
    bool IsSaturated,
    DateTimeOffset? UpdatedAtUtc);

public sealed record DashboardConcurrencyGroupDetail(
    DashboardConcurrencyGroupSummary Summary,
    IReadOnlyList<Guid> ClaimedJobIds,
    IReadOnlyList<Guid> BlockedJobIds);
```

The concurrency group list/detail views show:

- group key
- configured limit
- current occupancy
- blocked job count
- claimed jobs using the group
- queued jobs blocked by the group
- recent saturation state
- last updated timestamp

Requirements:

- V5 does not include UI controls or APIs to edit group limits.
- The dashboard must explain that group limit editing is deferred until the source-of-truth behavior between app config, runtime APIs, and restarts is specified.
- Group views must make it obvious when a job has no queue position because it is blocked by a saturated group.

## Worker Node Health

V5 adds worker node health views.

```csharp
public interface IDashboardNodeReader
{
    ValueTask<DashboardNodePage> ListNodesAsync(
        DashboardNodeQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DashboardNodeDetail?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardNodeQuery(
    DashboardNodeHealthState? State = null,
    int PageSize = 100,
    string? ContinuationToken = null);

public sealed record DashboardNodePage(
    IReadOnlyList<DashboardNodeSummary> Nodes,
    string? ContinuationToken);

public sealed record DashboardNodeSummary(
    string NodeId,
    DashboardNodeHealthState State,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    int ClaimedJobCount,
    int MaxConcurrentExecutionsPerNode,
    int CurrentExecutionCount);

public sealed record DashboardNodeDetail(
    DashboardNodeSummary Summary,
    IReadOnlyList<Guid> ClaimedJobIds);
```

The node list/detail views show:

- node id
- first seen timestamp
- last heartbeat timestamp
- health state
- claimed job count
- currently claimed jobs
- node-local max concurrency
- node-local current execution count

```csharp
public enum DashboardNodeHealthState
{
    Active,
    Stale,
    Dead
}
```

Requirements:

- Active/stale/dead state is derived from scheduler worker heartbeat records.
- Stale and dead thresholds are provider/runtime options with sensible defaults based on lease duration.
- Node health is about scheduler worker liveness, not full host machine observability.
- V5 does not require provider/database health views beyond surfacing failures that prevent node heartbeat or job claiming.

## Rolling Metrics

V5 adds in-dashboard rolling metrics.

```csharp
public interface IDashboardMetricsReader
{
    ValueTask<DashboardMetricsSnapshot> GetMetricsAsync(
        DashboardMetricsQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardMetricsQuery(
    IReadOnlyList<TimeSpan>? Windows = null);

public sealed record DashboardMetricsSnapshot(
    IReadOnlyList<DashboardMetricsWindow> Windows);

public sealed record DashboardMetricsWindow(
    TimeSpan Window,
    int QueuedCount,
    int ClaimedCount,
    int FailedCount,
    int CanceledCount,
    TimeSpan? OldestQueuedAge,
    double EnqueueRatePerMinute,
    double ClaimRatePerMinute,
    double SuccessRatePerMinute,
    double FailureRatePerMinute,
    double CancellationRatePerMinute,
    double RetryRatePerMinute,
    TimeSpan? P50QueueLatency,
    TimeSpan? P95QueueLatency,
    TimeSpan? P50ExecutionDuration,
    TimeSpan? P95ExecutionDuration,
    TimeSpan? P95ScheduleFireLag,
    int SaturatedConcurrencyGroupCount,
    int ActiveNodeCount,
    int StaleNodeCount,
    int DeadNodeCount);
```

Default metric windows:

- 5 minutes
- 1 hour
- 24 hours

Metrics include:

- queue depth by state
- oldest queued age
- enqueue rate
- claim rate
- success rate
- failure rate
- cancellation rate
- retry rate
- queue latency
- execution duration
- schedule fire lag
- concurrency group saturation
- worker heartbeat health

Requirements:

- Metrics are in-dashboard only in v5.
- V5 does not require OpenTelemetry, Prometheus, or external exporter integration.
- Providers may derive metrics from job rows/events or maintain aggregate tables, as long as the dashboard result is consistent with persisted scheduler state.
- Metrics must be calculated over rolling time windows, not only lifetime counters.

## Dashboard Events

V5 uses dashboard/system events to show what happened.

New event kinds:

- `RetryCloneRequested`
- `CancelRequested`
- `CancelObserved`
- `SchedulePaused`
- `ScheduleResumed`
- `ScheduleTriggered`
- `NodeBecameStale`
- `NodeBecameDead`
- `ConcurrencyGroupSaturated`

Requirements:

- Events record what happened, not who did it.
- Events must be durable and visible in the relevant job, schedule, node, or group detail view.
- Events remain subject to the v4 event retention model unless a provider defines longer retention for system events.

## Provider Requirements

A provider is v5 operations-compatible when it implements:

- v4 dashboard job read/event contracts
- job action contracts
- schedule read/action contracts
- concurrency group read contracts
- node health read contracts
- metrics read contracts
- schedule tags and inherited occurrence tags
- storage for retry clone linkage
- storage for cancellation request/observation timestamps
- worker node heartbeat records
- schedule occurrence history sufficient for schedule detail
- efficient derivation or storage of rolling metrics

PostgreSQL is expected to be the first v5 operations-compatible provider.

## Acceptance Scenarios

The v5 implementation is complete only when the following scenarios pass:

1. A failed job can be retried as a clone.
2. Retry clone creates a new linked job and leaves the original failed job unchanged.
3. Retry clone preserves descriptor, arguments, priority, concurrency groups, retry policy, and tags.
4. Retry clone ancestry is visible from the original and cloned job detail views.
5. Canceling queued, delayed, or retry-waiting jobs transitions them to `Canceled`.
6. Canceling a running job cancels the scheduler-owned execution token.
7. A running job that observes cancellation transitions to `Canceled` without retry.
8. A running job that ignores cancellation remains claimed and can still complete, fail, or expire normally.
9. Schedule pause prevents future automatic materialization and does not cancel existing jobs.
10. Schedule resume recomputes the next future fire time.
11. Trigger-now creates one immediate job from the schedule definition.
12. Trigger-now works even when the schedule is paused.
13. Trigger-now does not change the schedule's next automatic fire time.
14. Concurrency group views show configured limit, occupancy, blocked jobs, and saturation without exposing edit controls.
15. Node views show active, stale, and dead workers from heartbeat state.
16. Rolling metrics match persisted job/event data for the configured windows.
17. Dashboard action events explain what happened without requiring user attribution, reason fields, or audit infrastructure.
18. V5 adds no multi-tenancy, role/permission model, audit trail, job migration tooling, workflow engine, or external metrics exporter requirement.
