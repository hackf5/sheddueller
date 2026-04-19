namespace Sheddueller;

/// <summary>
/// Options applied to tasks materialized from a recurring schedule.
/// </summary>
public sealed record RecurringScheduleOptions(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    RetryPolicy? RetryPolicy = null,
    RecurringOverlapMode OverlapMode = RecurringOverlapMode.Skip);
