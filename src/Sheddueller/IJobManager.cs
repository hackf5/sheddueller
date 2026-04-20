namespace Sheddueller;

/// <summary>
/// Manages pending job instances.
/// </summary>
public interface IJobManager
{
    /// <summary>
    /// Cancels a queued job before it is claimed.
    /// </summary>
    ValueTask<bool> CancelAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
