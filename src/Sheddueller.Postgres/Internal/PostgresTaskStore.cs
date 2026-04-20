namespace Sheddueller.Postgres.Internal;

using Sheddueller.Dashboard;
using Sheddueller.Postgres.Internal.Operations;
using Sheddueller.Storage;

internal sealed class PostgresTaskStore(ShedduellerPostgresOptions options)
    : ITaskStore, IDashboardJobReader, IDashboardEventSink, IDashboardEventRetentionStore
{
    private readonly PostgresOperationContext _context = new(options);

    public ValueTask<EnqueueTaskResult> EnqueueAsync(
        EnqueueTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return EnqueueTaskOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<ClaimTaskResult> TryClaimNextAsync(
        ClaimTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return TryClaimNextTaskOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> MarkCompletedAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MarkTaskCompletedOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> MarkFailedAsync(
        FailTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MarkTaskFailedOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RenewTaskLeaseOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<bool> ReleaseTaskAsync(
        ReleaseTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ReleaseTaskOperation.ExecuteAsync(this._context, request, cancellationToken);
    }

    public ValueTask<int> RecoverExpiredLeasesAsync(
        RecoverExpiredLeasesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RecoverExpiredLeasesOperation.ExecuteAsync(this._context, cancellationToken);
    }

    public ValueTask<bool> CancelAsync(
        CancelTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CancelTaskOperation.ExecuteAsync(this._context, request, cancellationToken);
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
        Guid taskId,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.GetJobAsync(this._context, taskId, cancellationToken);

    public ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.GetQueuePositionAsync(this._context, taskId, cancellationToken);

    public IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid taskId,
        DashboardEventQuery? query = null,
        CancellationToken cancellationToken = default)
      => PostgresDashboardReadOperation.ReadEventsAsync(this._context, taskId, query, cancellationToken);

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
