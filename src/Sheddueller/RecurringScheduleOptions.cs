namespace Sheddueller;

/// <summary>
/// Options applied to jobs materialized from a recurring schedule.
/// </summary>
/// <param name="Priority">The queue priority assigned to materialized jobs. Higher values are claimed first.</param>
/// <param name="ConcurrencyGroupKeys">Optional dynamic concurrency groups assigned to materialized jobs.</param>
/// <param name="RetryPolicy">The retry policy assigned to materialized jobs.</param>
/// <param name="OverlapMode">Controls whether due occurrences are skipped when a previous occurrence is still active.</param>
/// <param name="Tags">Optional searchable metadata copied to materialized jobs.</param>
public sealed record RecurringScheduleOptions(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    RetryPolicy? RetryPolicy = null,
    RecurringOverlapMode OverlapMode = RecurringOverlapMode.Skip,
    IReadOnlyList<JobTag>? Tags = null);
