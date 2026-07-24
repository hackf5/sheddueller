namespace Sheddueller.Storage;

/// <summary>
/// Store request for clearing a concurrency-group code-defined default rate limit.
/// </summary>
public sealed record ClearConcurrencyDefaultRateLimitRequest(
    string GroupKey,
    DateTimeOffset UpdatedAtUtc);
