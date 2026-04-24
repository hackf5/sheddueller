namespace Sheddueller.Storage;

/// <summary>
/// Persisted classification for a job created from a recurring schedule.
/// </summary>
public enum ScheduleOccurrenceKind
{
    /// <summary>
    /// The job was created by normal recurring materialization.
    /// </summary>
    Automatic,

    /// <summary>
    /// The job was created by a dashboard trigger-now operation.
    /// </summary>
    ManualTrigger,
}
