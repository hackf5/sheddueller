namespace Sheddueller.Inspection.ConcurrencyGroups;

/// <summary>
/// A page of concurrency group inspection results.
/// </summary>
public sealed record ConcurrencyGroupInspectionPage(
    IReadOnlyList<ConcurrencyGroupInspectionSummary> Groups,
    string? ContinuationToken,
    long TotalCount);
