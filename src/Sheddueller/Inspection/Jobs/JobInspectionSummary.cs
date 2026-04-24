namespace Sheddueller.Inspection.Jobs;

using Sheddueller;
using Sheddueller.Storage;

/// <summary>
/// Job inspection list item.
/// </summary>
public sealed record JobInspectionSummary(
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
    IReadOnlyList<string> ConcurrencyGroupKeys,
    string? SourceScheduleKey,
    JobProgressSnapshot? LatestProgress,
    JobQueuePosition? QueuePosition,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? FailedAtUtc,
    DateTimeOffset? CanceledAtUtc)
{
    /// <summary>
    /// Original failed job id when this job was created by retry clone.
    /// </summary>
    public Guid? RetryCloneSourceJobId { get; init; }

    /// <summary>
    /// Job cancellation request timestamp for a claimed job.
    /// </summary>
    public DateTimeOffset? CancellationRequestedAtUtc { get; init; }

    /// <summary>
    /// Timestamp when the runtime observed cooperative cancellation.
    /// </summary>
    public DateTimeOffset? CancellationObservedAtUtc { get; init; }

    /// <summary>
    /// Schedule occurrence kind when the job was materialized from a schedule.
    /// </summary>
    public ScheduleOccurrenceKind? ScheduleOccurrenceKind { get; init; }
}
