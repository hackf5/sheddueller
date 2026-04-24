namespace Sheddueller.Inspection.Metrics;

/// <summary>
/// Metrics inspection rolling metrics for one time window.
/// </summary>
public sealed record MetricsInspectionWindow(
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
