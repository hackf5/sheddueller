namespace Sheddueller.Inspection.Jobs;

using Sheddueller.Storage;

/// <summary>
/// Reads job inspection metadata and durable job events.
/// </summary>
public interface IJobInspectionReader
{
    /// <summary>
    /// Reads job inspection overview data.
    /// </summary>
    ValueTask<JobInspectionOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches jobs using inspection filters.
    /// </summary>
    ValueTask<JobInspectionPage> SearchJobsAsync(
        JobInspectionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one job detail record.
    /// </summary>
    ValueTask<JobInspectionDetail?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the current queue position for a job.
    /// </summary>
    ValueTask<JobQueuePosition> GetQueuePositionAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads durable job events in ascending event sequence order.
    /// </summary>
    IAsyncEnumerable<JobEvent> ReadEventsAsync(
        Guid jobId,
        JobEventReadOptions? options = null,
        CancellationToken cancellationToken = default);
}
