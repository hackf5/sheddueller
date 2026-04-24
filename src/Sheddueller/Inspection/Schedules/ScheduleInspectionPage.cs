namespace Sheddueller.Inspection.Schedules;

/// <summary>
/// A page of recurring schedule inspection results.
/// </summary>
public sealed record ScheduleInspectionPage(
    IReadOnlyList<ScheduleInspectionSummary> Schedules,
    string? ContinuationToken,
    long TotalCount);
