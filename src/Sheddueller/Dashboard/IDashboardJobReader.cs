namespace Sheddueller.Dashboard;

/// <summary>
/// Reads dashboard job metadata and durable job events.
/// </summary>
public interface IDashboardJobReader
{
    /// <summary>
    /// Reads dashboard overview data.
    /// </summary>
    ValueTask<DashboardJobOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches jobs using dashboard filters.
    /// </summary>
    ValueTask<DashboardJobPage> SearchJobsAsync(
        DashboardJobQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one job detail record.
    /// </summary>
    ValueTask<DashboardJobDetail?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the current queue position for a job.
    /// </summary>
    ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads durable job events in ascending event sequence order.
    /// </summary>
    IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid jobId,
        DashboardEventQuery? query = null,
        CancellationToken cancellationToken = default);
}
