namespace Sheddueller.Storage;

/// <summary>
/// Store request for setting a concurrency-group code-defined default rate limit.
/// </summary>
public sealed record SetConcurrencyDefaultRateLimitRequest(
    string GroupKey,
    ConcurrencyGroupRateLimit RateLimit,
    DateTimeOffset UpdatedAtUtc);
