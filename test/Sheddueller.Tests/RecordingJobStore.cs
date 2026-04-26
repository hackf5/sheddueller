namespace Sheddueller.Tests;

using Sheddueller.Storage;

internal sealed class RecordingJobStore : IJobStore
{
    private readonly List<EnqueueJobRequest> enqueuedRequests = [];
    private readonly List<TriggerRecurringScheduleRequest> triggerRequests = [];
    private long nextSequence;

    public IReadOnlyList<EnqueueJobRequest> EnqueuedRequests => this.enqueuedRequests;

    public IReadOnlyList<TriggerRecurringScheduleRequest> TriggerRequests => this.triggerRequests;

    public RecurringScheduleTriggerResult TriggerResult { get; set; } = new(RecurringScheduleTriggerStatus.NotFound);

    public EnqueueJobRequest GetRequest(Guid jobId)
      => this.enqueuedRequests.Single(request => request.JobId == jobId);

    public ValueTask<EnqueueJobResult> EnqueueAsync(
        EnqueueJobRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.enqueuedRequests.Add(request);

        return ValueTask.FromResult(new EnqueueJobResult(request.JobId, ++this.nextSequence));
    }

    public ValueTask<IReadOnlyList<EnqueueJobResult>> EnqueueManyAsync(
        IReadOnlyList<EnqueueJobRequest> requests,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = requests.ToArray();
        var results = new EnqueueJobResult[snapshot.Length];

        for (var i = 0; i < snapshot.Length; i++)
        {
            var request = snapshot[i];
            this.enqueuedRequests.Add(request);
            results[i] = new EnqueueJobResult(request.JobId, ++this.nextSequence);
        }

        return ValueTask.FromResult<IReadOnlyList<EnqueueJobResult>>(results);
    }

    public ValueTask<ClaimJobResult> TryClaimNextAsync(
        ClaimJobRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> MarkCompletedAsync(
        CompleteJobRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> MarkFailedAsync(
        FailJobRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> ReleaseJobAsync(
        ReleaseJobRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<int> RecoverExpiredLeasesAsync(
        RecoverExpiredLeasesRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<JobCancellationResult> CancelAsync(
        CancelJobRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<DateTimeOffset?> GetCancellationRequestedAtAsync(
        JobCancellationStatusRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> MarkCancellationObservedAsync(
        ObserveJobCancellationRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask RecordWorkerNodeHeartbeatAsync(
        WorkerNodeHeartbeatRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask SetConcurrencyLimitAsync(
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
        UpsertRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<RecurringScheduleTriggerResult> TriggerRecurringScheduleAsync(
        TriggerRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.triggerRequests.Add(request);

        return ValueTask.FromResult(this.TriggerResult);
    }

    public ValueTask<bool> DeleteRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> PauseRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset pausedAtUtc,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<bool> ResumeRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset resumedAtUtc,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<RecurringScheduleInfo?> GetRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<IReadOnlyList<RecurringScheduleInfo>> ListRecurringSchedulesAsync(
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask<int> MaterializeDueRecurringSchedulesAsync(
        MaterializeDueRecurringSchedulesRequest request,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    private static NotSupportedException CreateUnsupportedException()
      => new("This test store only records enqueue requests.");
}
