namespace Sheddueller.Storage;

/// <summary>
/// Persisted state of a Sheddueller task.
/// </summary>
public enum TaskState
{
    /// <summary>
    /// The task is available for claiming.
    /// </summary>
    Queued,

    /// <summary>
    /// The task is owned by a node and is counted against its concurrency groups.
    /// </summary>
    Claimed,

    /// <summary>
    /// The task completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The task failed.
    /// </summary>
    Failed,
}
