namespace Sheddueller.Dashboard;

/// <summary>
/// Identifies a column available to a configured dashboard job view.
/// </summary>
public enum ShedduellerDashboardJobColumnKind
{
    /// <summary>The job identifier and detail-page link.</summary>
    JobId,

    /// <summary>The enqueue timestamp.</summary>
    Enqueued,

    /// <summary>The completed, failed, or canceled timestamp.</summary>
    TerminalTime,

    /// <summary>The current job state.</summary>
    State,

    /// <summary>The operational queue position.</summary>
    Queue,

    /// <summary>The job handler.</summary>
    Handler,

    /// <summary>Tags not promoted into their own columns.</summary>
    Tags,

    /// <summary>The latest reported progress.</summary>
    Progress,

    /// <summary>The current operational disposition.</summary>
    Disposition,

    /// <summary>Concurrency group keys.</summary>
    Groups,

    /// <summary>The job priority.</summary>
    Priority,

    /// <summary>The current and maximum attempt counts.</summary>
    Attempts,

    /// <summary>Values extracted from a named job tag.</summary>
    Tag,
}
