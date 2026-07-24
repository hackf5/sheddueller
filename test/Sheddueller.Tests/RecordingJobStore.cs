namespace Sheddueller.Tests;

using Sheddueller.Storage;

internal sealed class RecordingJobStore : IJobStore
{
    private readonly List<EnqueueJobRequest> enqueuedRequests = [];
    private readonly List<UpsertRecurringScheduleRequest> recurringScheduleRequests = [];
    private readonly List<TriggerRecurringScheduleRequest> triggerRequests = [];
    private readonly List<CancelQueuedJobsRequest> cancelQueuedJobsRequests = [];
    private readonly List<SetConcurrencyLimitRequest> concurrencyLimitRequests = [];
    private readonly List<SetConcurrencyDefaultLimitRequest> concurrencyDefaultLimitRequests = [];
    private readonly List<ClearConcurrencyLimitOverrideRequest> clearConcurrencyLimitOverrideRequests = [];
    private readonly List<SetConcurrencyRateLimitRequest> concurrencyRateLimitRequests = [];
    private readonly List<SetConcurrencyDefaultRateLimitRequest> concurrencyDefaultRateLimitRequests = [];
    private readonly List<ClearConcurrencyDefaultRateLimitRequest> clearConcurrencyDefaultRateLimitRequests = [];
    private readonly List<SetConcurrencyUnlimitedRateLimitRequest> concurrencyUnlimitedRateLimitRequests = [];
    private readonly List<ClearConcurrencyRateLimitOverrideRequest> clearConcurrencyRateLimitOverrideRequests = [];
    private long nextSequence;

    public IReadOnlyList<EnqueueJobRequest> EnqueuedRequests => this.enqueuedRequests;

    public IReadOnlyList<UpsertRecurringScheduleRequest> RecurringScheduleRequests => this.recurringScheduleRequests;

    public IReadOnlyList<TriggerRecurringScheduleRequest> TriggerRequests => this.triggerRequests;

    public IReadOnlyList<CancelQueuedJobsRequest> CancelQueuedJobsRequests => this.cancelQueuedJobsRequests;

    public IReadOnlyList<SetConcurrencyLimitRequest> ConcurrencyLimitRequests => this.concurrencyLimitRequests;

    public IReadOnlyList<SetConcurrencyDefaultLimitRequest> ConcurrencyDefaultLimitRequests => this.concurrencyDefaultLimitRequests;

    public IReadOnlyList<ClearConcurrencyLimitOverrideRequest> ClearConcurrencyLimitOverrideRequests => this.clearConcurrencyLimitOverrideRequests;

    public IReadOnlyList<SetConcurrencyRateLimitRequest> ConcurrencyRateLimitRequests => this.concurrencyRateLimitRequests;

    public IReadOnlyList<SetConcurrencyDefaultRateLimitRequest> ConcurrencyDefaultRateLimitRequests => this.concurrencyDefaultRateLimitRequests;

    public IReadOnlyList<ClearConcurrencyDefaultRateLimitRequest> ClearConcurrencyDefaultRateLimitRequests => this.clearConcurrencyDefaultRateLimitRequests;

    public IReadOnlyList<SetConcurrencyUnlimitedRateLimitRequest> ConcurrencyUnlimitedRateLimitRequests => this.concurrencyUnlimitedRateLimitRequests;

    public IReadOnlyList<ClearConcurrencyRateLimitOverrideRequest> ClearConcurrencyRateLimitOverrideRequests => this.clearConcurrencyRateLimitOverrideRequests;

    public RecurringScheduleUpsertResult CreateOrUpdateRecurringScheduleResult { get; set; } = RecurringScheduleUpsertResult.Created;

    public RecurringScheduleTriggerResult TriggerResult { get; set; } = new(RecurringScheduleTriggerStatus.NotFound);

    public int CancelQueuedJobsResult { get; set; }

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

    public ValueTask<int> CancelQueuedJobsAsync(
        CancelQueuedJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.cancelQueuedJobsRequests.Add(request);

        return ValueTask.FromResult(this.CancelQueuedJobsResult);
    }

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
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.concurrencyLimitRequests.Add(request);

        return ValueTask.CompletedTask;
    }

    public ValueTask SetConcurrencyDefaultLimitAsync(
        SetConcurrencyDefaultLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.concurrencyDefaultLimitRequests.Add(request);

        return ValueTask.CompletedTask;
    }

    public ValueTask ClearConcurrencyLimitOverrideAsync(
        ClearConcurrencyLimitOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.clearConcurrencyLimitOverrideRequests.Add(request);

        return ValueTask.CompletedTask;
    }

    public ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => throw CreateUnsupportedException();

    public ValueTask SetConcurrencyRateLimitAsync(
        SetConcurrencyRateLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.concurrencyRateLimitRequests.Add(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetConcurrencyDefaultRateLimitAsync(
        SetConcurrencyDefaultRateLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.concurrencyDefaultRateLimitRequests.Add(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearConcurrencyDefaultRateLimitAsync(
        ClearConcurrencyDefaultRateLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.clearConcurrencyDefaultRateLimitRequests.Add(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetConcurrencyUnlimitedRateLimitAsync(
        SetConcurrencyUnlimitedRateLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.concurrencyUnlimitedRateLimitRequests.Add(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearConcurrencyRateLimitOverrideAsync(
        ClearConcurrencyRateLimitOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.clearConcurrencyRateLimitOverrideRequests.Add(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ConcurrencyGroupRateLimitOverride> GetConcurrencyRateLimitOverrideAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => ValueTask.FromResult(new ConcurrencyGroupRateLimitOverride(ConcurrencyGroupRateLimitOverrideKind.Inherit));

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
        UpsertRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.recurringScheduleRequests.Add(request);

        return ValueTask.FromResult(this.CreateOrUpdateRecurringScheduleResult);
    }

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
