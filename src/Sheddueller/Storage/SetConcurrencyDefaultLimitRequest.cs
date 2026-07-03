namespace Sheddueller.Storage;

/// <summary>
/// Store request for setting a code-defined concurrency-group default limit.
/// </summary>
public sealed record SetConcurrencyDefaultLimitRequest(
    string GroupKey,
    int Limit,
    DateTimeOffset UpdatedAtUtc);
