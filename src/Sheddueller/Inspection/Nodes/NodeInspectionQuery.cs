namespace Sheddueller.Inspection.Nodes;

/// <summary>
/// Worker node inspection query.
/// </summary>
public sealed record NodeInspectionQuery(
    NodeHealthState? State = null,
    int PageSize = 100,
    string? ContinuationToken = null);
