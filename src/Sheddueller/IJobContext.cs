namespace Sheddueller;

/// <summary>
/// Runtime context optionally injected into job handlers.
/// </summary>
public interface IJobContext
{
    /// <summary>
    /// Gets the job identifier for the running job.
    /// </summary>
    Guid JobId { get; }

    /// <summary>
    /// Gets the one-based attempt number currently being executed.
    /// </summary>
    int AttemptNumber { get; }

    /// <summary>
    /// Gets the scheduler-owned execution cancellation token.
    /// </summary>
    CancellationToken CancellationToken { get; }
}
