namespace Sheddueller;

/// <summary>
/// Manages job instances.
/// </summary>
public interface IJobManager
{
    /// <summary>
    /// Requests cancellation for a job. Queued jobs are canceled immediately; running jobs observe the request cooperatively.
    /// </summary>
    ValueTask<JobCancellationResult> CancelAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
