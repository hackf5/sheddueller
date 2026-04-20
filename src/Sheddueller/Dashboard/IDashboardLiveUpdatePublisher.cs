namespace Sheddueller.Dashboard;

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
