namespace Sheddueller.Dashboard;

/// <summary>
/// A page of dashboard job search results.
/// </summary>
public sealed record DashboardJobPage(
    IReadOnlyList<DashboardJobSummary> Jobs,
    string? ContinuationToken)
{
    /// <summary>
    /// Total number of jobs matching the query, excluding continuation-token paging.
    /// </summary>
    public long TotalCount { get; init; } = Jobs.Count;
}
