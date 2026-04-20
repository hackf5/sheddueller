namespace Sheddueller.Dashboard;

/// <summary>
/// Cleans up retained dashboard event data.
/// </summary>
public interface IDashboardEventRetentionStore
{
    /// <summary>
    /// Deletes events whose terminal owning job is older than the retention period.
    /// </summary>
    ValueTask<int> CleanupAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}
