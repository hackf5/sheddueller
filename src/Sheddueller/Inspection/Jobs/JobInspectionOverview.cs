namespace Sheddueller.Inspection.Jobs;

using Sheddueller.Storage;

/// <summary>
/// Job inspection overview data.
/// </summary>
public sealed record JobInspectionOverview(
    IReadOnlyDictionary<JobState, int> StateCounts,
    IReadOnlyList<JobInspectionSummary> RunningJobs,
    IReadOnlyList<JobInspectionSummary> RecentlyFailedJobs,
    IReadOnlyList<JobInspectionSummary> QueuedJobs,
    IReadOnlyList<JobInspectionSummary> DelayedJobs,
    IReadOnlyList<JobInspectionSummary> RetryWaitingJobs);
