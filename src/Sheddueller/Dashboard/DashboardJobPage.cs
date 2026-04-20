namespace Sheddueller.Dashboard;

/// <summary>
/// A page of dashboard job search results.
/// </summary>
public sealed record DashboardJobPage(
    IReadOnlyList<DashboardJobSummary> Jobs,
    string? ContinuationToken);
