namespace Sheddueller.Storage;

/// <summary>
/// Stores task state and performs atomic claim selection.
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// Enqueues a new task and assigns its enqueue sequence.
    /// </summary>
    ValueTask<EnqueueTaskResult> EnqueueAsync(
        EnqueueTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the next globally eligible task, if one is available.
    /// </summary>
    ValueTask<ClaimTaskResult> TryClaimNextAsync(
        ClaimTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed task as completed.
    /// </summary>
    ValueTask MarkCompletedAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed task as failed.
    /// </summary>
    ValueTask MarkFailedAsync(
        FailTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a configured concurrency-group limit.
    /// </summary>
    ValueTask SetConcurrencyLimitAsync(
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the configured concurrency-group limit, if one exists.
    /// </summary>
    ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}
