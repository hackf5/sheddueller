namespace Sheddueller.Dashboard;

using Sheddueller.Storage;

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
