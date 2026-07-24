namespace Sheddueller.Inspection.ConcurrencyGroups;

/// <summary>
/// Concurrency group inspection list item.
/// </summary>
public sealed record ConcurrencyGroupInspectionSummary(
    string GroupKey,
    int EffectiveLimit,
    int CurrentOccupancy,
    int BlockedJobCount,
    bool IsSaturated,
    DateTimeOffset? UpdatedAtUtc)
{
    /// <summary>
    /// Gets the code-defined default limit, if one exists.
    /// </summary>
    public int? DefaultLimit { get; init; }

    /// <summary>
    /// Gets the live override limit, if one exists.
    /// </summary>
    public int? OverrideLimit { get; init; }

    /// <summary>
    /// Gets the code-defined default start rate, if one exists.
    /// </summary>
    public ConcurrencyGroupRateLimit? DefaultRateLimit { get; init; }

    /// <summary>
    /// Gets whether a live start-rate override exists.
    /// </summary>
    public bool HasRateLimitOverride { get; init; }

    /// <summary>
    /// Gets the live start-rate override. Null with <see cref="HasRateLimitOverride"/> set means explicitly unlimited.
    /// </summary>
    public ConcurrencyGroupRateLimit? OverrideRateLimit { get; init; }

    /// <summary>
    /// Gets the effective start rate, or null when starts are unlimited.
    /// </summary>
    public ConcurrencyGroupRateLimit? EffectiveRateLimit { get; init; }

    /// <summary>
    /// Gets the theoretical next permitted start time, if rate state has been consumed.
    /// </summary>
    public DateTimeOffset? NextRatePermitAtUtc { get; init; }

    /// <summary>
    /// Gets whether the group is currently waiting for its next rate permit.
    /// </summary>
    public bool IsRateLimited { get; init; }

    /// <summary>
    /// Gets the number of due queued jobs blocked by the concurrency limit.
    /// </summary>
    public int ConcurrencyBlockedJobCount { get; init; }

    /// <summary>
    /// Gets the number of due queued jobs blocked by the start-rate limit.
    /// </summary>
    public int RateBlockedJobCount { get; init; }
}
