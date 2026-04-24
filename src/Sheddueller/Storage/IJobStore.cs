namespace Sheddueller.Storage;

/// <summary>
/// Stores job state and performs atomic claim selection.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Enqueues a new job and assigns its enqueue sequence.
    /// </summary>
    ValueTask<EnqueueJobResult> EnqueueAsync(
        EnqueueJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically enqueues new jobs and assigns their enqueue sequences.
    /// </summary>
    ValueTask<IReadOnlyList<EnqueueJobResult>> EnqueueManyAsync(
        IReadOnlyList<EnqueueJobRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the next globally eligible job, if one is available.
    /// </summary>
    ValueTask<ClaimJobResult> TryClaimNextAsync(
        ClaimJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed job as completed.
    /// </summary>
    ValueTask<bool> MarkCompletedAsync(
        CompleteJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed job as failed.
    /// </summary>
    ValueTask<bool> MarkFailedAsync(
        FailJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a claimed job lease.
    /// </summary>
    ValueTask<bool> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a scheduler-interrupted job back to the queue without consuming retry budget.
    /// </summary>
    ValueTask<bool> ReleaseJobAsync(
        ReleaseJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers expired claimed jobs.
    /// </summary>
    ValueTask<int> RecoverExpiredLeasesAsync(
        RecoverExpiredLeasesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation for a job.
    /// </summary>
    ValueTask<JobCancellationResult> CancelAsync(
        CancelJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the cooperative cancellation request timestamp for a currently claimed job.
    /// </summary>
    ValueTask<DateTimeOffset?> GetCancellationRequestedAtAsync(
        JobCancellationStatusRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed job as canceled after the handler observes cooperative cancellation.
    /// </summary>
    ValueTask<bool> MarkCancellationObservedAsync(
        ObserveJobCancellationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records scheduler worker node liveness.
    /// </summary>
    ValueTask RecordWorkerNodeHeartbeatAsync(
        WorkerNodeHeartbeatRequest request,
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
