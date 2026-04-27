namespace Sheddueller.Storage;

/// <summary>
/// Identifies the execution-time source for a job method parameter.
/// </summary>
public enum JobMethodParameterBindingKind
{
    /// <summary>
    /// The parameter value is deserialized from the persisted argument payload.
    /// </summary>
    Serialized = 0,

    /// <summary>
    /// The parameter value is the scheduler-owned execution cancellation token.
    /// </summary>
    CancellationToken = 1,

    /// <summary>
    /// The parameter value is the runtime job context.
    /// </summary>
    JobContext = 2,

    /// <summary>
    /// The parameter value is resolved from dependency injection at execution time.
    /// </summary>
    Service = 3,

    /// <summary>
    /// The parameter value is the scheduler-owned job progress reporter.
    /// </summary>
    ProgressReporter = 4,
}
