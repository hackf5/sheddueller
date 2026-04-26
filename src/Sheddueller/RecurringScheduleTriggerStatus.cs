namespace Sheddueller;

/// <summary>
/// Result classification for a manual recurring schedule trigger.
/// </summary>
public enum RecurringScheduleTriggerStatus
{
    /// <summary>
    /// The schedule template was cloned into a queued job.
    /// </summary>
    Enqueued,

    /// <summary>
    /// No recurring schedule exists for the supplied key.
    /// </summary>
    NotFound,

    /// <summary>
    /// The schedule uses skip-overlap semantics and already has a queued or claimed occurrence.
    /// </summary>
    SkippedActiveOccurrence,
}
