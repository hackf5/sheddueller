namespace Microsoft.Extensions.Logging;

internal static partial class ShedduellerWorkerLoggerMessages
{
    private const int EventIdStart = 1100;

    [LoggerMessage(
        EventIdStart + 0,
        LogLevel.Information,
        "Sheddueller worker node {NodeId} started.")]
    public static partial void WorkerStarted(
        this ILogger logger,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 1,
        LogLevel.Information,
        "Sheddueller worker node {NodeId} stopped.")]
    public static partial void WorkerStopped(
        this ILogger logger,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 2,
        LogLevel.Error,
        "Sheddueller worker node {NodeId} stopped unexpectedly.")]
    public static partial void WorkerFailed(
        this ILogger logger,
        Exception exception,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 10,
        LogLevel.Debug,
        "Claimed job {JobId} for attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobClaimed(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 20,
        LogLevel.Debug,
        "Completed job {JobId} attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobCompleted(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 21,
        LogLevel.Error,
        "Job {JobId} attempt {AttemptNumber} failed on node {NodeId}.")]
    public static partial void JobFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 30,
        LogLevel.Information,
        "Cancellation was observed for job {JobId} attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobCancellationObserved(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 31,
        LogLevel.Warning,
        "Released job {JobId} attempt {AttemptNumber} on node {NodeId} before completion.")]
    public static partial void JobReleased(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 32,
        LogLevel.Warning,
        "Lost lease for job {JobId} attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobLeaseRenewalLost(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 33,
        LogLevel.Information,
        "Cancellation was requested for job {JobId} attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobCancellationRequestedObserved(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 34,
        LogLevel.Warning,
        "Heartbeat task failed for job {JobId} attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobHeartbeatFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        int attemptNumber,
        string nodeId);

    [LoggerMessage(
        EventIdStart + 40,
        LogLevel.Information,
        "Worker node {NodeId} recovered {RecoveredCount} expired leases and materialized {MaterializedCount} recurring schedule occurrences.")]
    public static partial void WorkerPeriodicStoreWorkCompleted(
        this ILogger logger,
        string nodeId,
        int recoveredCount,
        int materializedCount);
}
