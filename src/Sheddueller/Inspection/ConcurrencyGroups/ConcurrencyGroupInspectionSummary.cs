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
    DateTimeOffset? UpdatedAtUtc);
