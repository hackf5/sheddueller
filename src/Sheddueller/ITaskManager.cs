namespace Sheddueller;

/// <summary>
/// Manages pending task instances.
/// </summary>
public interface ITaskManager
{
    /// <summary>
    /// Cancels a queued task before it is claimed.
    /// </summary>
    ValueTask<bool> CancelAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);
}
