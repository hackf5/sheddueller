namespace Sheddueller.Inspection.Schedules;

using Sheddueller;

/// <summary>
/// Recurring schedule inspection detail.
/// </summary>
public sealed record ScheduleInspectionDetail(
    ScheduleInspectionSummary Summary,
    RetryPolicy? RetryPolicy,
    IReadOnlyList<ScheduleInspectionOccurrence> RecentOccurrences,
    ScheduleInspectionOccurrence? LastSuccessfulOccurrence,
    ScheduleInspectionOccurrence? LastFailedOccurrence);
