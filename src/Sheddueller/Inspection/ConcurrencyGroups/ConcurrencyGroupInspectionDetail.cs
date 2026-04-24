namespace Sheddueller.Inspection.ConcurrencyGroups;

/// <summary>
/// Concurrency group inspection detail.
/// </summary>
public sealed record ConcurrencyGroupInspectionDetail(
    ConcurrencyGroupInspectionSummary Summary,
    IReadOnlyList<Guid> ClaimedJobIds,
    IReadOnlyList<Guid> BlockedJobIds);
