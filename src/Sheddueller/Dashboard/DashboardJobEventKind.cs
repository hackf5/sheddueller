namespace Sheddueller.Dashboard;

/// <summary>
/// Dashboard job event kind.
/// </summary>
public enum DashboardJobEventKind
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
}
