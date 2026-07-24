namespace Sheddueller;

/// <summary>
/// Describes the live rate-limit override configured for a concurrency group.
/// </summary>
/// <param name="Kind">The override kind.</param>
/// <param name="RateLimit">The configured rate when <paramref name="Kind"/> is <see cref="ConcurrencyGroupRateLimitOverrideKind.Limited"/>.</param>
public sealed record ConcurrencyGroupRateLimitOverride(
    ConcurrencyGroupRateLimitOverrideKind Kind,
    ConcurrencyGroupRateLimit? RateLimit = null);
