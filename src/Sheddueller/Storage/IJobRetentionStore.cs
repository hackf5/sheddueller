namespace Sheddueller.Storage;

/// <summary>
/// Cleans up terminal jobs from the operational store.
/// </summary>
public interface IJobRetentionStore
{
    /// <summary>
    /// Deletes terminal jobs older than their configured cutoff timestamps.
    /// </summary>
    ValueTask<JobRetentionCleanupResult> CleanupTerminalJobsAsync(
        JobRetentionCleanupRequest request,
        CancellationToken cancellationToken = default);
}
