namespace Sheddueller.Storage;

/// <summary>
/// Store request for clearing a concurrency-group live rate-limit override.
/// </summary>
public sealed record ClearConcurrencyRateLimitOverrideRequest(
    string GroupKey,
    DateTimeOffset UpdatedAtUtc);
