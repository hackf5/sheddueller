namespace Microsoft.Extensions.Logging;

internal static partial class ShedduellerDashboardLoggerMessages
{
    private const int EventIdStart = 1300;

    [LoggerMessage(
        EventIdStart + 0,
        LogLevel.Debug,
        "Dashboard job-event listener service started with {ListenerCount} listeners.")]
    public static partial void DashboardJobEventListenerServiceStarted(
        this ILogger logger,
        int listenerCount);

    [LoggerMessage(
        EventIdStart + 10,
        LogLevel.Warning,
        "Dashboard failed to publish live job event {EventSequence} for job {JobId}.")]
    public static partial void DashboardJobEventPublishFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        long eventSequence);

    [LoggerMessage(
        EventIdStart + 20,
        LogLevel.Debug,
        "Dashboard job-event retention cleanup skipped because no retention store is registered.")]
    public static partial void DashboardEventRetentionStoreMissing(
        this ILogger logger);

    [LoggerMessage(
        EventIdStart + 21,
        LogLevel.Information,
        "Dashboard job-event retention cleanup deleted {DeletedCount} events.")]
    public static partial void DashboardEventRetentionCleaned(
        this ILogger logger,
        int deletedCount);

    [LoggerMessage(
        EventIdStart + 22,
        LogLevel.Warning,
        "Dashboard job-event retention cleanup failed.")]
    public static partial void DashboardEventRetentionCleanupFailed(
        this ILogger logger,
        Exception exception);
}
