namespace Sheddueller.Dashboard;

/// <summary>
/// Dashboard job detail.
/// </summary>
public sealed record DashboardJobDetail(
    DashboardJobSummary Summary,
    DateTimeOffset? ClaimedAtUtc,
    string? ClaimedByNodeId,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? ScheduledFireAtUtc,
    IReadOnlyList<DashboardJobEvent> RecentEvents);
