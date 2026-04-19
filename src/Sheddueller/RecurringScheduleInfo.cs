namespace Sheddueller;

/// <summary>
/// Current definition and state of a recurring schedule.
/// </summary>
public sealed record RecurringScheduleInfo(
    string ScheduleKey,
    string CronExpression,
    bool IsPaused,
    RecurringOverlapMode OverlapMode,
    int Priority,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    RetryPolicy? RetryPolicy,
    DateTimeOffset? NextFireAtUtc);
