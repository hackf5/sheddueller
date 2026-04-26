namespace Sheddueller;

/// <summary>
/// Result of manually triggering a recurring schedule.
/// </summary>
/// <param name="Status">The trigger outcome.</param>
/// <param name="JobId">The created job id when the trigger enqueued a job.</param>
/// <param name="EnqueueSequence">The created job enqueue sequence when supplied by storage.</param>
public sealed record RecurringScheduleTriggerResult(
    RecurringScheduleTriggerStatus Status,
    Guid? JobId = null,
    long? EnqueueSequence = null);
