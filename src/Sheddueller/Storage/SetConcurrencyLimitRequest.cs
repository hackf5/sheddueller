#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Store request for setting a concurrency-group limit.
/// </summary>
public sealed record SetConcurrencyLimitRequest(
    string GroupKey,
    int Limit,
    DateTimeOffset UpdatedAtUtc);
