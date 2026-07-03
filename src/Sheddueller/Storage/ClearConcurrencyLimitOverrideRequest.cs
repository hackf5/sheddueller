namespace Sheddueller.Storage;

/// <summary>
/// Store request for clearing a concurrency-group live override limit.
/// </summary>
public sealed record ClearConcurrencyLimitOverrideRequest(
    string GroupKey,
    DateTimeOffset UpdatedAtUtc);
