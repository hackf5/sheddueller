namespace Sheddueller.Storage;

/// <summary>
/// Cleans up retained job event data.
/// </summary>
public interface IJobEventRetentionStore
{
    /// <summary>
    /// Deletes events whose terminal owning job is older than the retention period.
    /// </summary>
    ValueTask<int> CleanupAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}
