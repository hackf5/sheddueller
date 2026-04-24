namespace Sheddueller.Storage;

/// <summary>
/// Durable job event kind.
/// </summary>
public enum JobEventKind
{
    /// <summary>
    /// Scheduler lifecycle event.
    /// </summary>
    Lifecycle,

    /// <summary>
    /// Job attempt started.
    /// </summary>
    AttemptStarted,

    /// <summary>
    /// Job attempt completed.
    /// </summary>
    AttemptCompleted,

    /// <summary>
    /// Job attempt failed.
    /// </summary>
    AttemptFailed,

    /// <summary>
    /// User job log event.
    /// </summary>
    Log,

    /// <summary>
    /// User job progress event.
    /// </summary>
    Progress,

    /// <summary>
    /// Cooperative cancellation was requested for a running job.
    /// </summary>
    CancelRequested,

    /// <summary>
    /// Cooperative cancellation was observed by the runtime.
    /// </summary>
    CancelObserved,
}
