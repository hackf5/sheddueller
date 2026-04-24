namespace Sheddueller.Inspection.Schedules;

using Sheddueller;

/// <summary>
/// Recurring schedule inspection search query.
/// </summary>
public sealed record ScheduleInspectionQuery(
    string? ScheduleKey = null,
    bool? IsPaused = null,
    string? ServiceType = null,
    string? MethodName = null,
    JobTag? Tag = null,
    int PageSize = 100,
    string? ContinuationToken = null);
