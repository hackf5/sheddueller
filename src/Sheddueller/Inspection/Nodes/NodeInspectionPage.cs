namespace Sheddueller.Inspection.Nodes;

/// <summary>
/// A page of worker node inspection results.
/// </summary>
public sealed record NodeInspectionPage(
    IReadOnlyList<NodeInspectionSummary> Nodes,
    string? ContinuationToken,
    long TotalCount);
