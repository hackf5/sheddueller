namespace Sheddueller.Inspection.Nodes;

/// <summary>
/// Worker node inspection detail.
/// </summary>
public sealed record NodeInspectionDetail(
    NodeInspectionSummary Summary,
    IReadOnlyList<Guid> ClaimedJobIds);
