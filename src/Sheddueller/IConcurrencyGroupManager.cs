namespace Sheddueller;

/// <summary>
/// Manages dynamic cluster-wide concurrency group limits.
/// </summary>
public interface IConcurrencyGroupManager
{
    /// <summary>
    /// Sets the live override limit for a concurrency group.
    /// </summary>
    ValueTask SetLimitAsync(
        string groupKey,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the code-defined default limit for a concurrency group without clearing a live override.
    /// </summary>
    ValueTask SetDefaultLimitAsync(
        string groupKey,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the live override limit for a concurrency group, falling back to the code-defined or built-in default.
    /// </summary>
    ValueTask ClearLimitOverrideAsync(
        string groupKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the live override limit for a concurrency group, if one exists.
    /// </summary>
    ValueTask<int?> GetConfiguredLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}
