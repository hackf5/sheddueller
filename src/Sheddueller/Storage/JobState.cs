namespace Sheddueller.Storage;

/// <summary>
/// Persisted state of a Sheddueller job.
/// </summary>
public enum JobState
{
    /// <summary>
    /// The job is available for claiming.
    /// </summary>
    Queued,

    /// <summary>
    /// The job is owned by a node and is counted against its concurrency groups.
    /// </summary>
    Claimed,

    /// <summary>
    /// The job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The job was canceled before it was claimed.
    /// </summary>
    Canceled,
}
