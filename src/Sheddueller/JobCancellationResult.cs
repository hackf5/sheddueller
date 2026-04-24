namespace Sheddueller;

/// <summary>
/// Result of a best-effort job cancellation request.
/// </summary>
public enum JobCancellationResult
{
    /// <summary>
    /// The queued job was canceled before it was claimed.
    /// </summary>
    Canceled,

    /// <summary>
    /// The running job received a cooperative cancellation request.
    /// </summary>
    CancellationRequested,

    /// <summary>
    /// The job was already in a terminal state.
    /// </summary>
    AlreadyFinished,

    /// <summary>
    /// No job with the requested id exists.
    /// </summary>
    NotFound,
}
