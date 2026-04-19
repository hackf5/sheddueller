# Sheddueller v4 Specification

Status: Accepted for implementation  
Last updated: 2026-04-19

## Relationship To Earlier Versions

This document extends [v1](v1-spec.md), [v2](v2-spec.md), and [v3](v3-spec.md). V4 adds a jobs dashboard and the runtime telemetry needed to make that dashboard useful while a job is still running.

V4 is intentionally scoped to observation, not operation. The dashboard is read-only and focused on jobs.

## Summary

Sheddueller v4 is about a first-class dashboard.

V4 is defined by six capabilities:

- An embedded ASP.NET Core/Blazor dashboard package.
- Provider-agnostic dashboard read/event contracts.
- Searchable job tags.
- Durable job logs and progress events.
- Live dashboard updates through SignalR.
- Dynamic queue-position visibility for queued jobs.

V4 exists to solve two concrete operator problems:

- A running job must be able to explain what it is doing now.
- A queued job must be easy to find and must show where it sits in the real scheduler queue when that is meaningful.

## Goals

- Provide an opt-in embedded dashboard that host applications can mount into an ASP.NET Core app.
- Keep the dashboard provider-agnostic so any storage provider can implement the dashboard contracts and become dashboard-compatible.
- Let running jobs emit durable logs and progress updates without relying on external logging infrastructure.
- Make jobs searchable by explicit caller-provided tags and scheduler metadata.
- Show dynamic queue position using the same ordering semantics the scheduler uses to claim work.
- Keep v4 read-only and defer operator actions to later versions.

## Non-Goals

- A standalone dashboard application.
- Schedule management or schedule views.
- Cluster/node health views.
- Concurrency-group management views.
- Mutating operator actions such as retry, cancel, requeue, delete, pause, resume, or edit.
- Payload-body inspection or argument search.
- Built-in authentication or authorization.
- Replacing application logging or observability platforms.

## Dashboard Hosting

V4 ships an embedded dashboard package, assumed to be `Sheddueller.Dashboard`.

The dashboard is implemented as a Razor Class Library containing:

- routable Razor components
- static web assets
- dashboard service registrations
- a SignalR hub for live updates
- endpoint mapping extensions

The dashboard should use Blazor interactive server rendering in v4. This keeps the UI running in the host process, allows components to call server-side dashboard services directly, and supports live updates over SignalR.

Reference platform documentation:

- Blazor render modes: <https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0>
- Blazor hosting models: <https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0>

### Registration

```csharp
public static class ShedduellerDashboardServiceCollectionExtensions
{
    public static IServiceCollection AddShedduellerDashboard(
        this IServiceCollection services,
        Action<ShedduellerDashboardOptions>? configure = null);
}

public static class ShedduellerDashboardEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapShedduellerDashboard(
        this IEndpointRouteBuilder endpoints,
        string path = "/sheddueller");
}

public sealed class ShedduellerDashboardOptions
{
    public TimeSpan EventRetention { get; set; } = TimeSpan.FromDays(7);
}
```

Requirements:

- The dashboard must be opt-in. Registering the package must not automatically expose routes.
- `MapShedduellerDashboard` mounts the dashboard UI and live-update hub under the configured path.
- `EventRetention` controls durable dashboard event retention after a task reaches a terminal state.
- `EventRetention` must be positive.

## Security Model

V4 does not implement its own authentication or authorization system.

Requirements:

- Host applications are responsible for protecting the dashboard route with ASP.NET Core authentication/authorization, reverse proxy rules, network isolation, or equivalent controls.
- The dashboard documentation must state that exposing the dashboard without protection can leak operational and business-sensitive metadata.
- Dashboard components must never display opaque serialized payload bodies.
- Dashboard components must display only scheduler metadata, tags, log events, progress events, and lifecycle events.

## Dashboard Contracts

Dashboard contracts live in the dashboard package or a dashboard abstractions package, not in the core scheduler package.

Storage providers that support the dashboard must implement these contracts.

### Dashboard Reads

```csharp
public interface IDashboardJobReader
{
    ValueTask<DashboardJobPage> SearchJobsAsync(
        DashboardJobQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DashboardJobDetail?> GetJobAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid taskId,
        DashboardEventQuery? query = null,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardJobQuery(
    Guid? TaskId = null,
    TaskState? State = null,
    string? ServiceType = null,
    string? MethodName = null,
    JobTag? Tag = null,
    string? SourceScheduleKey = null,
    DateTimeOffset? EnqueuedFromUtc = null,
    DateTimeOffset? EnqueuedToUtc = null,
    DateTimeOffset? TerminalFromUtc = null,
    DateTimeOffset? TerminalToUtc = null,
    int PageSize = 100,
    string? ContinuationToken = null);

public sealed record DashboardJobPage(
    IReadOnlyList<DashboardJobSummary> Jobs,
    string? ContinuationToken);

public sealed record DashboardJobSummary(
    Guid TaskId,
    TaskState State,
    string ServiceType,
    string MethodName,
    int Priority,
    long EnqueueSequence,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? NotBeforeUtc,
    int AttemptCount,
    int MaxAttempts,
    IReadOnlyList<JobTag> Tags,
    string? SourceScheduleKey,
    DashboardProgressSnapshot? LatestProgress,
    DashboardQueuePosition? QueuePosition,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? FailedAtUtc,
    DateTimeOffset? CanceledAtUtc);

public sealed record DashboardJobDetail(
    DashboardJobSummary Summary,
    DateTimeOffset? ClaimedAtUtc,
    string? ClaimedByNodeId,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? ScheduledFireAtUtc,
    IReadOnlyList<DashboardJobEvent> RecentEvents);

public sealed record DashboardProgressSnapshot(
    double? Percent,
    string? Message,
    DateTimeOffset ReportedAtUtc);

public sealed record DashboardEventQuery(
    long? AfterEventSequence = null,
    int Limit = 500);
```

Requirements:

- `SearchJobsAsync` is used by the jobs list/search page.
- `GetJobAsync` is used by the job detail page.
- `GetQueuePositionAsync` must report either a queue position or a reason why the job has no meaningful position.
- `ReadEventsAsync` must return durable events in ascending event sequence order.
- `DashboardJobQuery.PageSize` must be positive.
- `DashboardEventQuery.Limit` must be positive.
- Dashboard readers must not return opaque serialized payload bodies.

### Dashboard Event Sink

```csharp
public interface IDashboardEventSink
{
    ValueTask AppendAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken = default);
}
```

Requirements:

- Runtime code uses `IDashboardEventSink` to persist lifecycle, log, and progress events.
- Providers may implement event append and event read with the same underlying tables, streams, or documents.
- Events are durable and must survive process restarts until removed by retention.

### Live Updates

```csharp
public interface IDashboardLiveUpdatePublisher
{
    ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken = default);
}
```

Requirements:

- The dashboard package owns a SignalR hub for browser live updates.
- The runtime publishes new durable dashboard events to the live update publisher after they are persisted.
- Live updates are an optimization. Reloading the page must reconstruct the same job logs, progress, and timeline from durable events.
- Missed SignalR messages must not lose job history.

## Job Tags

V4 adds searchable job tags to task submission metadata.

```csharp
public sealed record JobTag(
    string Name,
    string Value);

public sealed record TaskSubmission(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    DateTimeOffset? NotBeforeUtc = null,
    RetryPolicy? RetryPolicy = null,
    IReadOnlyList<JobTag>? Tags = null);
```

Requirements:

- Tags are normalized key/value string pairs.
- Tag names and values must be non-empty after trimming.
- Tag names are case-sensitive.
- Tag values are case-sensitive.
- Duplicate name/value pairs must be deduplicated before persistence.
- Tags are caller-provided search metadata, not serialized payload.
- The dashboard must support searching by exact tag name and exact tag value.

Examples:

- `listing_id = 23`
- `tenant = acme`
- `import_batch = 2026-04-19-a`

The scheduler already knows task id, handler type, method name, source schedule key, state, and timestamps. Callers should use tags only for domain identifiers that cannot be derived from scheduler metadata.

## Job Context

V4 adds optional job-context-aware handler invocation.

`CancellationToken` remains the required special runtime parameter from v2. `IJobContext` is an optional special runtime parameter used only when the invoked handler method accepts it.

```csharp
public interface IJobContext
{
    Guid TaskId { get; }
    int AttemptNumber { get; }
    CancellationToken CancellationToken { get; }

    ValueTask LogAsync(
        JobLogLevel level,
        string message,
        IReadOnlyDictionary<string, string>? fields = null,
        CancellationToken cancellationToken = default);

    ValueTask ReportProgressAsync(
        double? percent,
        string? message = null,
        CancellationToken cancellationToken = default);
}

public enum JobLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
```

### Expression Shape

V2 handler expressions remain valid:

```csharp
Expression<Func<TService, CancellationToken, Task>>
Expression<Func<TService, CancellationToken, ValueTask>>
```

V4 additionally allows:

```csharp
Expression<Func<TService, CancellationToken, IJobContext, Task>>
Expression<Func<TService, CancellationToken, IJobContext, ValueTask>>
```

Requirements:

- `ITaskEnqueuer` and `IRecurringScheduleManager` must both support the job-context-aware expression shapes.
- `IJobContext` is optional. Handlers that do not report logs or progress do not need to accept it.
- When the lambda includes `IJobContext`, the expression must pass the lambda's context parameter to the invoked service method.
- The expression parser treats `IJobContext` as runtime-owned and never serializes it as an argument payload.
- Callers must not capture an external `IJobContext`.
- The runtime injects the real job context at invocation time.
- The job context's `CancellationToken` is the same scheduler-owned execution token passed as the explicit handler cancellation token.
- The same expression rules apply to immediate, delayed, retried, and recurring-materialized jobs.

Example:

```csharp
await enqueuer.EnqueueAsync<IListingIndexer>(
    (service, cancellationToken, job) =>
        service.IndexListingAsync(23, cancellationToken, job),
    new TaskSubmission(
        Priority: 10,
        Tags: [new JobTag("listing_id", "23")]));
```

## Durable Job Events

The dashboard is backed by durable events.

```csharp
public sealed record DashboardJobEvent(
    Guid EventId,
    Guid TaskId,
    long EventSequence,
    DashboardJobEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);

public enum DashboardJobEventKind
{
    Lifecycle,
    AttemptStarted,
    AttemptCompleted,
    AttemptFailed,
    Log,
    Progress
}
```

Requirements:

- `EventSequence` must be monotonic per task.
- Events for a task must be readable in ascending `EventSequence` order.
- Lifecycle events are emitted for important task state transitions.
- Attempt events are emitted when an attempt starts, completes, or fails.
- `IJobContext.LogAsync` appends a `Log` event.
- `IJobContext.ReportProgressAsync` appends a `Progress` event.
- Progress percent is optional. When present, it must be between `0` and `100` inclusive.
- The latest progress snapshot for a job is the newest `Progress` event by event sequence.
- Fields are structured string key/value pairs.
- Payload bodies and serialized method arguments are never stored in dashboard events.
- Providers must support retention cleanup for events older than `EventRetention` after the owning task reaches a terminal state.

## Queue Position

V4 defines queue position as dynamic claim-order position.

```csharp
public sealed record DashboardQueuePosition(
    Guid TaskId,
    DashboardQueuePositionKind Kind,
    long? Position,
    string? Reason = null);

public enum DashboardQueuePositionKind
{
    Claimable,
    Delayed,
    RetryWaiting,
    BlockedByConcurrency,
    Claimed,
    Terminal,
    Canceled,
    NotFound
}
```

Requirements:

- `Claimable` means the job is queued, due now, and not blocked by concurrency groups.
- `Position` is 1-based among all currently claimable jobs in global claim order.
- Global claim order is the existing scheduler order: `Priority DESC, EnqueueSequence ASC`.
- Jobs that are delayed, retry-waiting, blocked, claimed, terminal, canceled, or missing must return the corresponding non-claimable kind and a human-readable reason.
- Queue position is dynamic and may change between refreshes.
- Queue position must be calculated against the whole scheduler queue, not only the current dashboard filter/search result.

## Dashboard Views

V4 includes jobs-only views.

### Jobs Overview

The overview page shows:

- counts by job state
- currently running jobs
- recently failed jobs
- queued jobs
- delayed jobs
- retry-waiting jobs

### Job Search

The job search/list page supports filtering by:

- task id
- state
- handler service type
- handler method name
- exact tag name/value pair
- source schedule key
- enqueued time range
- terminal time range

The list view shows at least:

- task id
- state
- handler service type
- handler method name
- priority
- enqueue time
- attempt count
- latest progress message or percent
- queue-position summary
- terminal timestamp when present

### Job Detail

The job detail page shows:

- task id
- handler service type and method name
- tags
- state
- priority and enqueue sequence
- enqueue, due, claim, lease, retry, and terminal timestamps
- attempt count and max attempts
- source schedule key and scheduled fire time when present
- current queue position or non-position reason
- latest progress snapshot
- live log stream
- durable event timeline

The job detail page must update live while a job is running and must reconstruct the same information from durable events after refresh.

## Read-Only Dashboard

V4 exposes no mutating controls.

The dashboard must not include:

- retry buttons
- cancel buttons
- force requeue controls
- delete controls
- schedule pause/resume controls
- schedule edit controls
- concurrency-limit edit controls
- running-job kill controls

Later versions may add operator actions after the authorization and audit model is specified.

## Provider Requirements

A provider is dashboard-compatible when it implements:

- dashboard job search/detail reads
- dashboard queue-position calculation
- durable dashboard event append/read
- durable event retention cleanup
- live update publishing after durable append
- indexed tag search

Provider implementations must not expose opaque serialized payload bodies to the dashboard.

PostgreSQL is expected to be the first dashboard-compatible durable provider.

## Acceptance Scenarios

The v4 implementation is complete only when the following scenarios pass:

1. The dashboard package registers services without automatically exposing routes.
2. `MapShedduellerDashboard("/sheddueller")` mounts the embedded Blazor dashboard under the configured path.
3. Host applications can protect the mounted route with their own ASP.NET Core authorization policy.
4. Jobs can be searched by exact string tag pairs such as `listing_id=23`.
5. Jobs can be searched by task id, state, handler service type, handler method, source schedule key, and time ranges.
6. Queue position matches scheduler claim ordering for currently claimable jobs.
7. Delayed, retry-waiting, blocked, claimed, terminal, canceled, and missing jobs return explicit non-position reasons.
8. `IJobContext` is injected when the invoked handler method accepts it.
9. `IJobContext` is never serialized as a task argument.
10. `IJobContext.LogAsync` writes durable timestamped log events with level, message, and structured fields.
11. `IJobContext.ReportProgressAsync` writes durable progress events and updates the latest progress snapshot.
12. Live job logs and progress appear in the dashboard while a job is running.
13. Refreshing the browser reconstructs logs, progress, and lifecycle timeline from durable events.
14. Event retention removes old dashboard events after the configured TTL once the owning task is terminal.
15. The dashboard never displays opaque serialized payload bodies.
16. The dashboard exposes no mutating job, schedule, or concurrency controls.

## Known Limitations

- V4 is jobs-only.
- V4 is read-only.
- V4 does not include schedule views.
- V4 does not include cluster or node health views.
- V4 does not include concurrency-group views.
- V4 does not implement built-in authentication or authorization.
- V4 does not search serialized payloads or method arguments.
- V4 does not replace external logging, tracing, or metrics systems.
