namespace Sheddueller.Inspection.ConcurrencyGroups;

/// <summary>
/// Concurrency group inspection search query.
/// </summary>
public sealed record ConcurrencyGroupInspectionQuery(
    string? GroupKey = null,
    bool? IsSaturated = null,
    bool? HasBlockedJobs = null,
    int PageSize = 100,
    string? ContinuationToken = null);
