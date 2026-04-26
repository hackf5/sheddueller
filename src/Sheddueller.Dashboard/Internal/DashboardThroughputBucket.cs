namespace Sheddueller.Dashboard.Internal;

internal sealed record DashboardThroughputBucket(
    DateTimeOffset StartedAtUtc,
    int QueuedCount,
    int StartedCount,
    int SucceededCount,
    int FailedCount,
    int CanceledCount,
    int FailedAttemptCount);
