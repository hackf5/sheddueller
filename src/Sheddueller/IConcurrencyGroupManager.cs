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

    /// <summary>
    /// Sets the live rate-limit override for a concurrency group.
    /// </summary>
    ValueTask SetRateLimitAsync(
        string groupKey,
        ConcurrencyGroupRateLimit rateLimit,
        CancellationToken cancellationToken = default)
      => throw new NotSupportedException("This concurrency-group manager does not support rate limits.");

    /// <summary>
    /// Sets the code-defined default rate limit for a concurrency group without clearing a live override.
    /// </summary>
    ValueTask SetDefaultRateLimitAsync(
        string groupKey,
        ConcurrencyGroupRateLimit rateLimit,
        CancellationToken cancellationToken = default)
      => throw new NotSupportedException("This concurrency-group manager does not support rate limits.");

    /// <summary>
    /// Clears the code-defined default rate limit for a concurrency group without clearing a live override.
    /// </summary>
    ValueTask ClearDefaultRateLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => throw new NotSupportedException("This concurrency-group manager does not support rate limits.");

    /// <summary>
    /// Sets an explicitly unlimited live rate override for a concurrency group.
    /// </summary>
    ValueTask SetUnlimitedRateLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => throw new NotSupportedException("This concurrency-group manager does not support rate limits.");

    /// <summary>
    /// Clears the live rate-limit override for a concurrency group, falling back to its code-defined default.
    /// </summary>
    ValueTask ClearRateLimitOverrideAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => throw new NotSupportedException("This concurrency-group manager does not support rate limits.");

    /// <summary>
    /// Gets the live rate-limit override for a concurrency group.
    /// </summary>
    ValueTask<ConcurrencyGroupRateLimitOverride> GetRateLimitOverrideAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => ValueTask.FromResult(new ConcurrencyGroupRateLimitOverride(ConcurrencyGroupRateLimitOverrideKind.Inherit));
}
