namespace Sheddueller.Inspection.Jobs;

using Sheddueller.Storage;

/// <summary>
/// Job inspection search query.
/// </summary>
public sealed record JobInspectionQuery(
    IReadOnlyList<JobState>? States = null,
    string? HandlerContains = null,
    string? TagContains = null,
    string? ConcurrencyGroupContains = null,
    int PageSize = 100,
    string? ContinuationToken = null);
