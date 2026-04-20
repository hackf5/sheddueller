namespace Sheddueller;

/// <summary>
/// Log severity for durable job log events.
/// </summary>
public enum JobLogLevel
{
    /// <summary>
    /// Highly detailed diagnostic information.
    /// </summary>
    Trace,

    /// <summary>
    /// Debug diagnostic information.
    /// </summary>
    Debug,

    /// <summary>
    /// Informational operational message.
    /// </summary>
    Information,

    /// <summary>
    /// Warning operational message.
    /// </summary>
    Warning,

    /// <summary>
    /// Error operational message.
    /// </summary>
    Error,

    /// <summary>
    /// Critical operational message.
    /// </summary>
    Critical,
}
