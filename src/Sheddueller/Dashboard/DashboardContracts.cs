namespace Sheddueller.Dashboard;

using Sheddueller.Storage;

/// <summary>
/// Reads dashboard job metadata and durable job events.
/// </summary>
public interface IDashboardJobReader
{
    /// <summary>
    /// Reads dashboard overview data.
    /// </summary>
    ValueTask<DashboardJobOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches jobs using dashboard filters.
    /// </summary>
    ValueTask<DashboardJobPage> SearchJobsAsync(
        DashboardJobQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one job detail record.
    /// </summary>
    ValueTask<DashboardJobDetail?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the current queue position for a job.
    /// </summary>
    ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads durable job events in ascending event sequence order.
    /// </summary>
    IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid jobId,
        DashboardEventQuery? query = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Appends durable dashboard job events.
/// </summary>
public interface IDashboardEventSink
{
    /// <summary>
    /// Persists an event and returns the provider-assigned durable event.
    /// </summary>
    ValueTask<DashboardJobEvent> AppendAsync(
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes dashboard events to live subscribers.
/// </summary>
public interface IDashboardLiveUpdatePublisher
{
    /// <summary>
    /// Publishes a persisted job event.
    /// </summary>
    ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cleans up retained dashboard event data.
/// </summary>
public interface IDashboardEventRetentionStore
{
    /// <summary>
    /// Deletes events whose terminal owning job is older than the retention period.
    /// </summary>
    ValueTask<int> CleanupAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dashboard job search query.
/// </summary>
public sealed record DashboardJobQuery(
    Guid? JobId = null,
    JobState? State = null,
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

/// <summary>
/// A page of dashboard job search results.
/// </summary>
public sealed record DashboardJobPage(
    IReadOnlyList<DashboardJobSummary> Jobs,
    string? ContinuationToken);

/// <summary>
/// Dashboard overview data for jobs.
/// </summary>
public sealed record DashboardJobOverview(
    IReadOnlyDictionary<JobState, int> StateCounts,
    IReadOnlyList<DashboardJobSummary> RunningJobs,
    IReadOnlyList<DashboardJobSummary> RecentlyFailedJobs,
    IReadOnlyList<DashboardJobSummary> QueuedJobs,
    IReadOnlyList<DashboardJobSummary> DelayedJobs,
    IReadOnlyList<DashboardJobSummary> RetryWaitingJobs);

/// <summary>
/// Dashboard job list item.
/// </summary>
public sealed record DashboardJobSummary(
    Guid JobId,
    JobState State,
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

/// <summary>
/// Dashboard job detail.
/// </summary>
public sealed record DashboardJobDetail(
    DashboardJobSummary Summary,
    DateTimeOffset? ClaimedAtUtc,
    string? ClaimedByNodeId,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? ScheduledFireAtUtc,
    IReadOnlyList<DashboardJobEvent> RecentEvents);

/// <summary>
/// Latest dashboard progress snapshot.
/// </summary>
public sealed record DashboardProgressSnapshot(
    double? Percent,
    string? Message,
    DateTimeOffset ReportedAtUtc);

/// <summary>
/// Dashboard event read query.
/// </summary>
public sealed record DashboardEventQuery(
    long? AfterEventSequence = null,
    int Limit = 500);

/// <summary>
/// Request to append a dashboard job event.
/// </summary>
public sealed record AppendDashboardJobEventRequest(
    Guid JobId,
    DashboardJobEventKind Kind,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// Durable dashboard job event.
/// </summary>
public sealed record DashboardJobEvent(
    Guid EventId,
    Guid JobId,
    long EventSequence,
    DashboardJobEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// Dashboard job event kind.
/// </summary>
public enum DashboardJobEventKind
{
    /// <summary>
    /// Scheduler lifecycle event.
    /// </summary>
    Lifecycle,

    /// <summary>
    /// Job attempt started.
    /// </summary>
    AttemptStarted,

    /// <summary>
    /// Job attempt completed.
    /// </summary>
    AttemptCompleted,

    /// <summary>
    /// Job attempt failed.
    /// </summary>
    AttemptFailed,

    /// <summary>
    /// User job log event.
    /// </summary>
    Log,

    /// <summary>
    /// User job progress event.
    /// </summary>
    Progress,
}

/// <summary>
/// Dynamic dashboard queue position.
/// </summary>
public sealed record DashboardQueuePosition(
    Guid JobId,
    DashboardQueuePositionKind Kind,
    long? Position,
    string? Reason = null);

/// <summary>
/// Dashboard queue position classification.
/// </summary>
public enum DashboardQueuePositionKind
{
    /// <summary>
    /// Job is currently claimable.
    /// </summary>
    Claimable,

    /// <summary>
    /// Job is delayed by its initial not-before timestamp.
    /// </summary>
    Delayed,

    /// <summary>
    /// Job is waiting for retry backoff.
    /// </summary>
    RetryWaiting,

    /// <summary>
    /// Job is blocked by concurrency group saturation.
    /// </summary>
    BlockedByConcurrency,

    /// <summary>
    /// Job is already claimed.
    /// </summary>
    Claimed,

    /// <summary>
    /// Job is terminal.
    /// </summary>
    Terminal,

    /// <summary>
    /// Job is canceled.
    /// </summary>
    Canceled,

    /// <summary>
    /// Job was not found.
    /// </summary>
    NotFound,
}
