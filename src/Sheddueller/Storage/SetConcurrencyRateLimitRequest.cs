namespace Sheddueller.Storage;

/// <summary>
/// Store request for setting a concurrency-group live rate-limit override.
/// </summary>
public sealed record SetConcurrencyRateLimitRequest(
    string GroupKey,
    ConcurrencyGroupRateLimit RateLimit,
    DateTimeOffset UpdatedAtUtc);
