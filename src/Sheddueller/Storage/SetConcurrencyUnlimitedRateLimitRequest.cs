namespace Sheddueller.Storage;

/// <summary>
/// Store request for setting an explicitly unlimited concurrency-group live rate override.
/// </summary>
public sealed record SetConcurrencyUnlimitedRateLimitRequest(
    string GroupKey,
    DateTimeOffset UpdatedAtUtc);
