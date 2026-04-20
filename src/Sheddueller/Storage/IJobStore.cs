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
    ValueTask<bool> MarkCompletedAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed task as failed.
    /// </summary>
    ValueTask<bool> MarkFailedAsync(
        FailTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a claimed task lease.
    /// </summary>
    ValueTask<bool> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a scheduler-interrupted task back to the queue without consuming retry budget.
    /// </summary>
    ValueTask<bool> ReleaseTaskAsync(
        ReleaseTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers expired claimed tasks.
    /// </summary>
    ValueTask<int> RecoverExpiredLeasesAsync(
        RecoverExpiredLeasesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a queued task.
    /// </summary>
    ValueTask<bool> CancelAsync(
        CancelTaskRequest request,
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

    /// <summary>
    /// Creates or updates a recurring schedule definition.
    /// </summary>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
        UpsertRecurringScheduleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a recurring schedule definition.
    /// </summary>
    ValueTask<bool> DeleteRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a recurring schedule.
    /// </summary>
    ValueTask<bool> PauseRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset pausedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a recurring schedule.
    /// </summary>
    ValueTask<bool> ResumeRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset resumedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a recurring schedule definition.
    /// </summary>
    ValueTask<RecurringScheduleInfo?> GetRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recurring schedule definitions.
    /// </summary>
    ValueTask<IReadOnlyList<RecurringScheduleInfo>> ListRecurringSchedulesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes due recurring schedule occurrences.
    /// </summary>
    ValueTask<int> MaterializeDueRecurringSchedulesAsync(
        MaterializeDueRecurringSchedulesRequest request,
        CancellationToken cancellationToken = default);
}
