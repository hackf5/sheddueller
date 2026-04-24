namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Job queue position classification.
/// </summary>
public enum JobQueuePositionKind
{
    /// <summary>
    /// Job is currently claimable.
    /// </summary>
    Claimable,

    /// <summary>
    /// Job is delayed by its initial not-before timestamp.
    /// </summary>
    Delayed,

    /// <summary>
    /// Job is waiting for retry backoff.
    /// </summary>
    RetryWaiting,

    /// <summary>
    /// Job is blocked by concurrency group saturation.
    /// </summary>
    BlockedByConcurrency,

    /// <summary>
    /// Job is already claimed.
    /// </summary>
    Claimed,

    /// <summary>
    /// Job is terminal.
    /// </summary>
    Terminal,

    /// <summary>
    /// Job is canceled.
    /// </summary>
    Canceled,

    /// <summary>
    /// Job was not found.
    /// </summary>
    NotFound,
}
