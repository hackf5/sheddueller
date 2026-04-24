namespace Sheddueller.Storage;

/// <summary>
/// Appends durable job events.
/// </summary>
public interface IJobEventSink
{
    /// <summary>
    /// Persists an event and returns the provider-assigned durable event.
    /// </summary>
    ValueTask<JobEvent> AppendAsync(
        AppendJobEventRequest request,
        CancellationToken cancellationToken = default);
}
