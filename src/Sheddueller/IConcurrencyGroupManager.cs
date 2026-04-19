namespace Sheddueller;

/// <summary>
/// Manages dynamic cluster-wide concurrency group limits.
/// </summary>
public interface IConcurrencyGroupManager
{
    /// <summary>
    /// Sets the configured limit for a concurrency group.
    /// </summary>
    ValueTask SetLimitAsync(
        string groupKey,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the configured limit for a concurrency group, if one exists.
    /// </summary>
    ValueTask<int?> GetConfiguredLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}
