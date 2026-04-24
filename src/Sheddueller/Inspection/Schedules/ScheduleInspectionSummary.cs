namespace Sheddueller.Inspection.Schedules;

using Sheddueller;

/// <summary>
/// Recurring schedule inspection list item.
/// </summary>
public sealed record ScheduleInspectionSummary(
    string ScheduleKey,
    string ServiceType,
    string MethodName,
    string CronExpression,
    bool IsPaused,
    RecurringOverlapMode OverlapMode,
    DateTimeOffset? NextFireAtUtc,
    int Priority,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    IReadOnlyList<JobTag> Tags,
    DateTimeOffset UpdatedAtUtc);
