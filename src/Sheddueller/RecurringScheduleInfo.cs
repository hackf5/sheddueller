namespace Sheddueller;

/// <summary>
/// Current definition and state of a recurring schedule.
/// </summary>
/// <param name="ScheduleKey">The stable unique key for the schedule.</param>
/// <param name="CronExpression">The standard five-field cron expression evaluated in UTC.</param>
/// <param name="IsPaused">Whether the schedule is currently paused.</param>
/// <param name="OverlapMode">Controls whether due occurrences may overlap.</param>
/// <param name="Priority">The priority assigned to materialized jobs.</param>
/// <param name="ConcurrencyGroupKeys">The concurrency groups assigned to materialized jobs.</param>
/// <param name="RetryPolicy">The retry policy assigned to materialized jobs.</param>
/// <param name="NextFireAtUtc">The next UTC occurrence time, or null when paused or unscheduled.</param>
public sealed record RecurringScheduleInfo(
    string ScheduleKey,
    string CronExpression,
    bool IsPaused,
    RecurringOverlapMode OverlapMode,
    int Priority,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    RetryPolicy? RetryPolicy,
    DateTimeOffset? NextFireAtUtc)
{
    /// <summary>
    /// Tags inherited by jobs materialized from this schedule.
    /// </summary>
    public IReadOnlyList<JobTag> Tags { get; init; } = [];
}
