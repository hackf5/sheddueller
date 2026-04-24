namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// A page of job inspection search results.
/// </summary>
public sealed record JobInspectionPage(
    IReadOnlyList<JobInspectionSummary> Jobs,
    string? ContinuationToken,
    long TotalCount);
