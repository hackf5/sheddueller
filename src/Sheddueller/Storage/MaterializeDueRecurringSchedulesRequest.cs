namespace Sheddueller.Storage;

/// <summary>
/// Store request for materializing due recurring schedule occurrences.
/// </summary>
public sealed record MaterializeDueRecurringSchedulesRequest(
    DateTimeOffset MaterializedAtUtc,
    RetryPolicy? DefaultRetryPolicy);
