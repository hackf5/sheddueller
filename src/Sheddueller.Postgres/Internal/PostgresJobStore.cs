namespace Sheddueller.Postgres.Internal;

using Sheddueller.Dashboard;
using Sheddueller.Postgres.Internal.Operations;
using Sheddueller.Storage;

internal sealed class PostgresJobStore(ShedduellerPostgresOptions options)
    : IJobStore, IDashboardJobReader, IDashboardEventSink, IDashboardEventRetentionStore
{
    private readonly PostgresOperationContext _context = new(options);

    public ValueTask<EnqueueJobResult> EnqueueAsync(
        EnqueueJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return EnqueueJobOperation.ExecuteAsync(this._context, request, cancellationToken);
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

    public ValueTask<bool> CancelAsync(
        CancelJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CancelJobOperation.ExecuteAsync(this._context, request, cancellationToken);
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

    public ValueTask<DashboardJobOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.GetOverviewAsync(this._context, cancellationToken);

    public ValueTask<DashboardJobPage> SearchJobsAsync(
        DashboardJobQuery query,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.SearchJobsAsync(this._context, query, cancellationToken);

    public ValueTask<DashboardJobDetail?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.GetJobAsync(this._context, jobId, cancellationToken);

    public ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.GetQueuePositionAsync(this._context, jobId, cancellationToken);

    public IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid jobId,
        DashboardEventQuery? query = null,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.ReadEventsAsync(this._context, jobId, query, cancellationToken);

    public ValueTask<DashboardJobEvent> AppendAsync(
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostgresDashboardEvents.AppendAsync(this._context, request, cancellationToken);
    }

    public ValueTask<int> CleanupAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.CleanupAsync(this._context, retention, cancellationToken);
}
