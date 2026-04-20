namespace Sheddueller.Dashboard;

/// <summary>
/// Appends durable dashboard job events.
/// </summary>
public interface IDashboardEventSink
{
    /// <summary>
    /// Persists an event and returns the provider-assigned durable event.
    /// </summary>
    ValueTask<DashboardJobEvent> AppendAsync(
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken = default);
}
