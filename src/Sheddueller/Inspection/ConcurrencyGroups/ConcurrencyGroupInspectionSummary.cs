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
}
