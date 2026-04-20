namespace Sheddueller.Dashboard;

using Sheddueller.Storage;

/// <summary>
/// Dashboard overview data for jobs.
/// </summary>
public sealed record DashboardJobOverview(
    IReadOnlyDictionary<JobState, int> StateCounts,
    IReadOnlyList<DashboardJobSummary> RunningJobs,
    IReadOnlyList<DashboardJobSummary> RecentlyFailedJobs,
    IReadOnlyList<DashboardJobSummary> QueuedJobs,
    IReadOnlyList<DashboardJobSummary> DelayedJobs,
    IReadOnlyList<DashboardJobSummary> RetryWaitingJobs);
