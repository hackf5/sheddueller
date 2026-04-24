namespace Sheddueller.Storage;

/// <summary>
/// Notifies subscribers about persisted job events.
/// </summary>
public interface IJobEventNotifier
{
    /// <summary>
    /// Notifies subscribers about a persisted job event.
    /// </summary>
    ValueTask NotifyAsync(
        JobEvent jobEvent,
        CancellationToken cancellationToken = default);
}
