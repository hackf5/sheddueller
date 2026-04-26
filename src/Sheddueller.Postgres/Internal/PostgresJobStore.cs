namespace Sheddueller.Postgres.Internal;

using Microsoft.Extensions.Options;

using Sheddueller;
using Sheddueller.Inspection.ConcurrencyGroups;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Inspection.Metrics;
using Sheddueller.Inspection.Nodes;
using Sheddueller.Inspection.Schedules;
using Sheddueller.Postgres.Internal.Operations;
using Sheddueller.Storage;

internal sealed class PostgresJobStore(
    ShedduellerPostgresOptions options,
    IOptions<ShedduellerOptions> shedduellerOptions)
    : IJobStore,
    IJobInspectionReader,
    IJobEventSink,
    IJobEventRetentionStore,
    IScheduleInspectionReader,
    IConcurrencyGroupInspectionReader,
    INodeInspectionReader,
    IMetricsInspectionReader
{
    private readonly PostgresOperationContext _context = new(options);
    private readonly IOptions<ShedduellerOptions> _shedduellerOptions = shedduellerOptions;

    public ValueTask<EnqueueJobResult> EnqueueAsync(
        EnqueueJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return EnqueueJobOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<IReadOnlyList<EnqueueJobResult>> EnqueueManyAsync(
        IReadOnlyList<EnqueueJobRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        return EnqueueJobOperation.ExecuteManyAsync(this._context, requests, cancellationToken);
    }

    public ValueTask<ClaimJobResult> TryClaimNextAsync(
        ClaimJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return TryClaimNextJobOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> MarkCompletedAsync(
        CompleteJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MarkJobCompletedOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> MarkFailedAsync(
        FailJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MarkJobFailedOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RenewJobLeaseOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> ReleaseJobAsync(
        ReleaseJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ReleaseJobOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<int> RecoverExpiredLeasesAsync(
        RecoverExpiredLeasesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RecoverExpiredLeasesOperation.ExecuteAsync(this._context, cancellationToken);
    }

    public ValueTask<JobCancellationResult> CancelAsync(
        CancelJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CancelJobOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<DateTimeOffset?> GetCancellationRequestedAtAsync(
        JobCancellationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostgresWorkerOperations.GetCancellationRequestedAtAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> MarkCancellationObservedAsync(
        ObserveJobCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostgresWorkerOperations.MarkCancellationObservedAsync(this._context, request, cancellationToken);
    }

    public ValueTask RecordWorkerNodeHeartbeatAsync(
        WorkerNodeHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostgresWorkerOperations.RecordWorkerNodeHeartbeatAsync(this._context, request, cancellationToken);
    }

    public ValueTask SetConcurrencyLimitAsync(
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SetConcurrencyLimitOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => GetConfiguredConcurrencyLimitOperation.ExecuteAsync(this._context, groupKey, cancellationToken);

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
        UpsertRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CreateOrUpdateRecurringScheduleOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<RecurringScheduleTriggerResult> TriggerRecurringScheduleAsync(
        TriggerRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return TriggerRecurringScheduleOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> DeleteRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
      => DeleteRecurringScheduleOperation.ExecuteAsync(this._context, scheduleKey, cancellationToken);

    public ValueTask<bool> PauseRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset pausedAtUtc,
        CancellationToken cancellationToken = default)
      => PauseRecurringScheduleOperation.ExecuteAsync(this._context, scheduleKey, cancellationToken);

    public ValueTask<bool> ResumeRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset resumedAtUtc,
        CancellationToken cancellationToken = default)
      => ResumeRecurringScheduleOperation.ExecuteAsync(this._context, scheduleKey, cancellationToken);

    public ValueTask<RecurringScheduleInfo?> GetRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
      => GetRecurringScheduleOperation.ExecuteAsync(this._context, scheduleKey, cancellationToken);

    public ValueTask<IReadOnlyList<RecurringScheduleInfo>> ListRecurringSchedulesAsync(
        CancellationToken cancellationToken = default)
      => ListRecurringSchedulesOperation.ExecuteAsync(this._context, cancellationToken);

    public ValueTask<int> MaterializeDueRecurringSchedulesAsync(
        MaterializeDueRecurringSchedulesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MaterializeDueRecurringSchedulesOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<JobInspectionOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
      => PostgresJobInspectionOperation.GetOverviewAsync(this._context, cancellationToken);

    public ValueTask<JobInspectionPage> SearchJobsAsync(
        JobInspectionQuery query,
        CancellationToken cancellationToken = default)
      => PostgresJobInspectionOperation.SearchJobsAsync(this._context, query, cancellationToken);

    public ValueTask<JobInspectionDetail?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
      => PostgresJobInspectionOperation.GetJobAsync(this._context, jobId, cancellationToken);

    public ValueTask<JobQueuePosition> GetQueuePositionAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
      => PostgresJobInspectionOperation.GetQueuePositionAsync(this._context, jobId, cancellationToken);

    public IAsyncEnumerable<JobEvent> ReadEventsAsync(
        Guid jobId,
        JobEventReadOptions? options = null,
        CancellationToken cancellationToken = default)
      => PostgresJobInspectionOperation.ReadEventsAsync(this._context, jobId, options, cancellationToken);

    public ValueTask<JobEvent> AppendAsync(
        AppendJobEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostgresJobEvents.AppendAsync(this._context, request, cancellationToken);
    }

    public ValueTask<int> CleanupAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default)
      => PostgresJobInspectionOperation.CleanupAsync(this._context, retention, cancellationToken);

    public ValueTask<ScheduleInspectionPage> SearchSchedulesAsync(
        ScheduleInspectionQuery query,
        CancellationToken cancellationToken = default)
      => PostgresScheduleInspectionOperation.SearchSchedulesAsync(this._context, query, cancellationToken);

    public ValueTask<ScheduleInspectionDetail?> GetScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
      => PostgresScheduleInspectionOperation.GetScheduleAsync(this._context, scheduleKey, cancellationToken);

    public ValueTask<ConcurrencyGroupInspectionPage> SearchConcurrencyGroupsAsync(
        ConcurrencyGroupInspectionQuery query,
        CancellationToken cancellationToken = default)
      => PostgresConcurrencyGroupInspectionOperation.SearchAsync(this._context, query, cancellationToken);

    public ValueTask<ConcurrencyGroupInspectionDetail?> GetConcurrencyGroupAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
      => PostgresConcurrencyGroupInspectionOperation.GetAsync(this._context, groupKey, cancellationToken);

    public ValueTask<NodeInspectionPage> SearchNodesAsync(
        NodeInspectionQuery query,
        CancellationToken cancellationToken = default)
      => PostgresNodeInspectionOperation.SearchAsync(
        this._context,
        query,
        this._shedduellerOptions.Value.EffectiveStaleNodeThreshold,
        this._shedduellerOptions.Value.EffectiveDeadNodeThreshold,
        cancellationToken);

    public ValueTask<NodeInspectionDetail?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
      => PostgresNodeInspectionOperation.GetAsync(
        this._context,
        nodeId,
        this._shedduellerOptions.Value.EffectiveStaleNodeThreshold,
        this._shedduellerOptions.Value.EffectiveDeadNodeThreshold,
        cancellationToken);

    public ValueTask<MetricsInspectionSnapshot> GetMetricsAsync(
        MetricsInspectionQuery query,
        CancellationToken cancellationToken = default)
      => PostgresMetricsInspectionOperation.GetAsync(
        this._context,
        query,
        this._shedduellerOptions.Value.EffectiveStaleNodeThreshold,
        this._shedduellerOptions.Value.EffectiveDeadNodeThreshold,
        cancellationToken);

}
