namespace Microsoft.Extensions.Logging;

internal static partial class ShedduellerLoggerMessages
{
    private const int EventIdStart = 1000;

    [LoggerMessage(
        EventIdStart + 0,
        LogLevel.Debug,
        "Enqueued job {JobId} with sequence {EnqueueSequence}.")]
    public static partial void JobEnqueued(
        this ILogger logger,
        Guid jobId,
        long enqueueSequence);

    [LoggerMessage(
        EventIdStart + 1,
        LogLevel.Debug,
        "Reused existing idempotent job {JobId} with sequence {EnqueueSequence}.")]
    public static partial void JobEnqueueDeduplicated(
        this ILogger logger,
        Guid jobId,
        long enqueueSequence);

    [LoggerMessage(
        EventIdStart + 2,
        LogLevel.Debug,
        "Submitted {SubmittedCount} jobs and enqueued {EnqueuedCount} new jobs.")]
    public static partial void JobsBatchEnqueued(
        this ILogger logger,
        int submittedCount,
        int enqueuedCount);

    [LoggerMessage(
        EventIdStart + 10,
        LogLevel.Debug,
        "Cancel request for job {JobId} returned {Result}.")]
    public static partial void JobCancellationRequested(
        this ILogger logger,
        Guid jobId,
        string result);

    [LoggerMessage(
        EventIdStart + 20,
        LogLevel.Debug,
        "Recurring schedule {ScheduleKey} upsert returned {Result}.")]
    public static partial void RecurringScheduleUpserted(
        this ILogger logger,
        string scheduleKey,
        string result);

    [LoggerMessage(
        EventIdStart + 21,
        LogLevel.Debug,
        "Recurring schedule {ScheduleKey} trigger returned {Status}.")]
    public static partial void RecurringScheduleTriggered(
        this ILogger logger,
        string scheduleKey,
        string status);

    [LoggerMessage(
        EventIdStart + 22,
        LogLevel.Debug,
        "Recurring schedule {ScheduleKey} delete returned {Deleted}.")]
    public static partial void RecurringScheduleDeleted(
        this ILogger logger,
        string scheduleKey,
        bool deleted);

    [LoggerMessage(
        EventIdStart + 23,
        LogLevel.Debug,
        "Recurring schedule {ScheduleKey} pause returned {Paused}.")]
    public static partial void RecurringSchedulePaused(
        this ILogger logger,
        string scheduleKey,
        bool paused);

    [LoggerMessage(
        EventIdStart + 24,
        LogLevel.Debug,
        "Recurring schedule {ScheduleKey} resume returned {Resumed}.")]
    public static partial void RecurringScheduleResumed(
        this ILogger logger,
        string scheduleKey,
        bool resumed);

    [LoggerMessage(
        EventIdStart + 30,
        LogLevel.Debug,
        "Set concurrency group {GroupKey} limit to {Limit}.")]
    public static partial void ConcurrencyGroupLimitSet(
        this ILogger logger,
        string groupKey,
        int limit);

    [LoggerMessage(
        EventIdStart + 40,
        LogLevel.Warning,
        "Failed to append durable job event for job {JobId}.")]
    public static partial void JobEventAppendFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId);
}
