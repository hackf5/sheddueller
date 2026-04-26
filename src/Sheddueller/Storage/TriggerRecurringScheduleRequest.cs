namespace Sheddueller.Storage;

/// <summary>
/// Store request for manually triggering a recurring schedule.
/// </summary>
public sealed record TriggerRecurringScheduleRequest(
    string ScheduleKey,
    RetryPolicy? DefaultRetryPolicy);
