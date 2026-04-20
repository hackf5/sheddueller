namespace Sheddueller;

using System.Globalization;
using System.Runtime.CompilerServices;

using Sheddueller.Dashboard;
using Sheddueller.Enqueueing;
using Sheddueller.Scheduling;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class InMemoryTaskStore(IDashboardLiveUpdatePublisher liveUpdatePublisher)
    : ITaskStore, IDashboardJobReader, IDashboardEventSink, IDashboardEventRetentionStore
{
    private readonly Lock _gate = new();
    private readonly IDashboardLiveUpdatePublisher _liveUpdatePublisher = liveUpdatePublisher;
    private readonly Dictionary<Guid, InMemoryTaskRecord> _tasks = [];
    private readonly List<DashboardJobEvent> _events = [];
    private readonly Dictionary<string, InMemoryRecurringScheduleRecord> _recurringSchedules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _concurrencyLimits = new(StringComparer.Ordinal);
    private long _nextEnqueueSequence;

    public InMemoryTaskStore()
      : this(new InMemoryNoOpDashboardLiveUpdatePublisher())
    {
    }

    public async ValueTask<EnqueueTaskResult> EnqueueAsync(
      EnqueueTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnqueueRequest(request);

        DashboardJobEvent jobEvent;
        EnqueueTaskResult result;
        lock (this._gate)
        {
            if (this._tasks.ContainsKey(request.TaskId))
            {
                throw new InvalidOperationException($"Task '{request.TaskId}' already exists.");
            }

            var enqueueSequence = ++this._nextEnqueueSequence;
            var task = CreateTaskRecord(request, enqueueSequence);
            this._tasks.Add(request.TaskId, task);
            jobEvent = this.AppendEventNoLock(
              task,
              DashboardJobEventKind.Lifecycle,
              attemptNumber: 0,
              message: "Queued");
            result = new EnqueueTaskResult(request.TaskId, enqueueSequence);
        }

        await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ClaimTaskResult> TryClaimNextAsync(
      ClaimTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.LeaseExpiresAtUtc <= request.ClaimedAtUtc)
        {
            throw new ArgumentException("Lease expiry must be after the claimed timestamp.", nameof(request));
        }

        DashboardJobEvent? jobEvent = null;
        ClaimTaskResult result = new ClaimTaskResult.NoTaskAvailable();
        lock (this._gate)
        {
            foreach (var task in this._tasks.Values
              .Where(task => task.State == TaskState.Queued)
              .Where(task => task.NotBeforeUtc is null || task.NotBeforeUtc <= request.ClaimedAtUtc)
              .OrderByDescending(task => task.Priority)
              .ThenBy(task => task.EnqueueSequence))
            {
                if (!this.CanClaim(task))
                {
                    continue;
                }

                task.State = TaskState.Claimed;
                task.AttemptCount++;
                task.ClaimedByNodeId = request.NodeId;
                task.ClaimedAtUtc = request.ClaimedAtUtc;
                task.LeaseToken = Guid.NewGuid();
                task.LeaseExpiresAtUtc = request.LeaseExpiresAtUtc;
                task.LastHeartbeatAtUtc = null;

                jobEvent = this.AppendEventNoLock(
                  task,
                  DashboardJobEventKind.AttemptStarted,
                  task.AttemptCount,
                  "Attempt started");
                result = new ClaimTaskResult.Claimed(CreateClaimedTask(task));
                break;
            }
        }

        if (jobEvent is not null)
        {
            await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async ValueTask<bool> MarkCompletedAsync(
      CompleteTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DashboardJobEvent> events;
        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.CompletedAtUtc, out var task))
            {
                return false;
            }

            task.State = TaskState.Completed;
            task.CompletedAtUtc = request.CompletedAtUtc;
            events =
            [
                this.AppendEventNoLock(task, DashboardJobEventKind.AttemptCompleted, task.AttemptCount, "Attempt completed"),
                this.AppendEventNoLock(task, DashboardJobEventKind.Lifecycle, task.AttemptCount, "Completed"),
            ];
        }

        await this.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> MarkFailedAsync(
      FailTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DashboardJobEvent> events;
        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.FailedAtUtc, out var task))
            {
                return false;
            }

            ApplyFailedAttempt(task, request.FailedAtUtc, request.Failure);
            events =
            [
                this.AppendEventNoLock(task, DashboardJobEventKind.AttemptFailed, task.AttemptCount, request.Failure.Message),
                this.AppendEventNoLock(task, DashboardJobEventKind.Lifecycle, task.AttemptCount, task.State == TaskState.Failed ? "Failed" : "Retry scheduled"),
            ];
        }

        await this.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask<bool> RenewLeaseAsync(
      RenewLeaseRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.LeaseExpiresAtUtc <= request.HeartbeatAtUtc)
        {
            throw new ArgumentException("Lease expiry must be after the heartbeat timestamp.", nameof(request));
        }

        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.HeartbeatAtUtc, out var task))
            {
                return ValueTask.FromResult(false);
            }

            task.LastHeartbeatAtUtc = request.HeartbeatAtUtc;
            task.LeaseExpiresAtUtc = request.LeaseExpiresAtUtc;

            return ValueTask.FromResult(true);
        }
    }

    public async ValueTask<bool> ReleaseTaskAsync(
      ReleaseTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        DashboardJobEvent jobEvent;
        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.ReleasedAtUtc, out var task))
            {
                return false;
            }

            var attemptNumber = task.AttemptCount;
            task.State = TaskState.Queued;
            task.AttemptCount = Math.Max(0, task.AttemptCount - 1);
            task.NotBeforeUtc = null;
            ClearClaim(task);
            jobEvent = this.AppendEventNoLock(task, DashboardJobEventKind.Lifecycle, attemptNumber, "Released");
        }

        await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<int> RecoverExpiredLeasesAsync(
      RecoverExpiredLeasesRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        List<DashboardJobEvent> events = [];
        int recovered;
        lock (this._gate)
        {
            recovered = 0;
            foreach (var task in this._tasks.Values
              .Where(task => task.State == TaskState.Claimed && task.LeaseExpiresAtUtc <= request.RecoveredAtUtc)
              .ToArray())
            {
                ApplyFailedAttempt(task, request.RecoveredAtUtc, new TaskFailureInfo(
                  "Sheddueller.LeaseExpired",
                  "The task lease expired before the owning node renewed it.",
                  null));
                events.Add(this.AppendEventNoLock(task, DashboardJobEventKind.AttemptFailed, task.AttemptCount, "The task lease expired before the owning node renewed it."));
                events.Add(this.AppendEventNoLock(task, DashboardJobEventKind.Lifecycle, task.AttemptCount, task.State == TaskState.Failed ? "Failed" : "Retry scheduled"));
                recovered++;
            }
        }

        await this.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        return recovered;
    }

    public async ValueTask<bool> CancelAsync(
      CancelTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        DashboardJobEvent jobEvent;
        lock (this._gate)
        {
            if (!this._tasks.TryGetValue(request.TaskId, out var task) || task.State != TaskState.Queued)
            {
                return false;
            }

            task.State = TaskState.Canceled;
            task.CanceledAtUtc = request.CanceledAtUtc;
            jobEvent = this.AppendEventNoLock(task, DashboardJobEventKind.Lifecycle, task.AttemptCount, "Canceled");
        }

        await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask SetConcurrencyLimitAsync(
      SetConcurrencyLimitRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateConcurrencyGroupKey(request.GroupKey);

        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Concurrency group limits must be positive.");
        }

        lock (this._gate)
        {
            this._concurrencyLimits[request.GroupKey] = request.Limit;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
      string groupKey,
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);

        lock (this._gate)
        {
            return ValueTask.FromResult(this._concurrencyLimits.TryGetValue(groupKey, out var limit) ? (int?)limit : null);
        }
    }

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
      UpsertRecurringScheduleRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRecurringScheduleRequest(request);

        lock (this._gate)
        {
            if (!this._recurringSchedules.TryGetValue(request.ScheduleKey, out var schedule))
            {
                this._recurringSchedules.Add(request.ScheduleKey, new InMemoryRecurringScheduleRecord(
                  request.ScheduleKey,
                  request.CronExpression,
                  request.ServiceType,
                  request.MethodName,
                  [.. request.MethodParameterTypes],
                  ClonePayload(request.SerializedArguments),
                  request.Priority,
                  [.. request.ConcurrencyGroupKeys],
                  request.RetryPolicy,
                  request.OverlapMode,
                  isPaused: false,
                  CronSchedule.GetNextOccurrenceAfter(request.CronExpression, request.UpsertedAtUtc)));

                return ValueTask.FromResult(RecurringScheduleUpsertResult.Created);
            }

            if (ScheduleDefinitionEquals(schedule, request))
            {
                return ValueTask.FromResult(RecurringScheduleUpsertResult.Unchanged);
            }

            schedule.CronExpression = request.CronExpression;
            schedule.ServiceType = request.ServiceType;
            schedule.MethodName = request.MethodName;
            schedule.MethodParameterTypes = [.. request.MethodParameterTypes];
            schedule.SerializedArguments = ClonePayload(request.SerializedArguments);
            schedule.Priority = request.Priority;
            schedule.ConcurrencyGroupKeys = [.. request.ConcurrencyGroupKeys];
            schedule.RetryPolicy = request.RetryPolicy;
            schedule.OverlapMode = request.OverlapMode;

            if (!schedule.IsPaused)
            {
                schedule.NextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(request.CronExpression, request.UpsertedAtUtc);
            }

            return ValueTask.FromResult(RecurringScheduleUpsertResult.Updated);
        }
    }

    public ValueTask<bool> DeleteRecurringScheduleAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateScheduleKey(scheduleKey);

        lock (this._gate)
        {
            return ValueTask.FromResult(this._recurringSchedules.Remove(scheduleKey));
        }
    }

    public ValueTask<bool> PauseRecurringScheduleAsync(
      string scheduleKey,
      DateTimeOffset pausedAtUtc,
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateScheduleKey(scheduleKey);

        lock (this._gate)
        {
            if (!this._recurringSchedules.TryGetValue(scheduleKey, out var schedule))
            {
                return ValueTask.FromResult(false);
            }

            schedule.IsPaused = true;
            schedule.NextFireAtUtc = null;

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> ResumeRecurringScheduleAsync(
      string scheduleKey,
      DateTimeOffset resumedAtUtc,
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateScheduleKey(scheduleKey);

        lock (this._gate)
        {
            if (!this._recurringSchedules.TryGetValue(scheduleKey, out var schedule))
            {
                return ValueTask.FromResult(false);
            }

            schedule.IsPaused = false;
            schedule.NextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(schedule.CronExpression, resumedAtUtc);

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<RecurringScheduleInfo?> GetRecurringScheduleAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateScheduleKey(scheduleKey);

        lock (this._gate)
        {
            return ValueTask.FromResult(this._recurringSchedules.TryGetValue(scheduleKey, out var schedule)
              ? CreateScheduleInfo(schedule)
              : null);
        }
    }

    public ValueTask<IReadOnlyList<RecurringScheduleInfo>> ListRecurringSchedulesAsync(
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            IReadOnlyList<RecurringScheduleInfo> schedules = [.. this._recurringSchedules.Values
              .OrderBy(schedule => schedule.ScheduleKey, StringComparer.Ordinal)
              .Select(CreateScheduleInfo)];

            return ValueTask.FromResult(schedules);
        }
    }

    public async ValueTask<int> MaterializeDueRecurringSchedulesAsync(
      MaterializeDueRecurringSchedulesRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateRetryPolicy(request.DefaultRetryPolicy);

        List<DashboardJobEvent> events = [];
        int materialized;
        lock (this._gate)
        {
            materialized = 0;

            foreach (var schedule in this._recurringSchedules.Values
              .Where(schedule => !schedule.IsPaused)
              .Where(schedule => schedule.NextFireAtUtc is not null && schedule.NextFireAtUtc <= request.MaterializedAtUtc)
              .OrderBy(schedule => schedule.ScheduleKey, StringComparer.Ordinal)
              .ToArray())
            {
                var scheduledFireAtUtc = schedule.NextFireAtUtc.GetValueOrDefault();
                var canMaterialize = schedule.OverlapMode == RecurringOverlapMode.Allow
                  || !this._tasks.Values.Any(task =>
                    string.Equals(task.SourceScheduleKey, schedule.ScheduleKey, StringComparison.Ordinal)
                    && task.State is TaskState.Queued or TaskState.Claimed);

                if (canMaterialize)
                {
                    var (maxAttempts, retryBackoffKind, retryBaseDelay, retryMaxDelay) = SubmissionValidator.NormalizeRetryPolicy(
                      schedule.RetryPolicy ?? request.DefaultRetryPolicy);
                    var taskId = Guid.NewGuid();
                    var enqueueSequence = ++this._nextEnqueueSequence;
                    var task = new InMemoryTaskRecord(
                      taskId,
                      TaskState.Queued,
                      schedule.Priority,
                      enqueueSequence,
                      request.MaterializedAtUtc,
                      schedule.ServiceType,
                      schedule.MethodName,
                      [.. schedule.MethodParameterTypes],
                      ClonePayload(schedule.SerializedArguments),
                      [.. schedule.ConcurrencyGroupKeys],
                      null,
                      maxAttempts,
                      retryBackoffKind,
                      retryBaseDelay,
                      retryMaxDelay,
                      schedule.ScheduleKey,
                      scheduledFireAtUtc,
                      tags: []);
                    this._tasks.Add(taskId, task);
                    events.Add(this.AppendEventNoLock(task, DashboardJobEventKind.Lifecycle, attemptNumber: 0, "Queued"));
                    materialized++;
                }

                schedule.NextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(schedule.CronExpression, request.MaterializedAtUtc);
            }
        }

        await this.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        return materialized;
    }

    public ValueTask<DashboardJobOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            var now = DateTimeOffset.UtcNow;
            var summaries = this._tasks.Values
              .Select(task => this.CreateSummaryNoLock(task, now))
              .ToArray();
            var stateCounts = Enum.GetValues<TaskState>()
              .ToDictionary(state => state, state => summaries.Count(summary => summary.State == state));

            return ValueTask.FromResult(new DashboardJobOverview(
              stateCounts,
              [.. summaries
                .Where(summary => summary.State == TaskState.Claimed)
                .OrderByDescending(summary => summary.EnqueuedAtUtc)
                .Take(10)],
              [.. summaries
                .Where(summary => summary.State == TaskState.Failed)
                .OrderByDescending(summary => summary.FailedAtUtc)
                .Take(10)],
              [.. summaries
                .Where(summary => summary.QueuePosition?.Kind == DashboardQueuePositionKind.Claimable)
                .OrderByDescending(summary => summary.Priority)
                .ThenBy(summary => summary.EnqueueSequence)
                .Take(10)],
              [.. summaries
                .Where(summary => summary.QueuePosition?.Kind == DashboardQueuePositionKind.Delayed)
                .OrderBy(summary => summary.NotBeforeUtc)
                .Take(10)],
              [.. summaries
                .Where(summary => summary.QueuePosition?.Kind == DashboardQueuePositionKind.RetryWaiting)
                .OrderBy(summary => summary.NotBeforeUtc)
                .Take(10)]));
        }
    }

    public ValueTask<DashboardJobPage> SearchJobsAsync(
        DashboardJobQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateJobQuery(query);

        lock (this._gate)
        {
            var now = DateTimeOffset.UtcNow;
            var afterSequence = DecodeContinuationToken(query.ContinuationToken);
            var matched = this._tasks.Values
              .Where(task => MatchesQuery(task, query))
              .Where(task => afterSequence is null || task.EnqueueSequence < afterSequence.Value)
              .OrderByDescending(task => task.EnqueueSequence)
              .Take(query.PageSize + 1)
              .ToArray();
            var pageItems = matched.Take(query.PageSize).ToArray();
            var continuationToken = matched.Length > query.PageSize
              ? pageItems[^1].EnqueueSequence.ToString(CultureInfo.InvariantCulture)
              : null;

            return ValueTask.FromResult(new DashboardJobPage(
              [.. pageItems.Select(task => this.CreateSummaryNoLock(task, now))],
              continuationToken));
        }
    }

    public ValueTask<DashboardJobDetail?> GetJobAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            if (!this._tasks.TryGetValue(taskId, out var task))
            {
                return ValueTask.FromResult<DashboardJobDetail?>(null);
            }

            var recentEvents = this._events
              .Where(jobEvent => jobEvent.TaskId == taskId)
              .OrderByDescending(jobEvent => jobEvent.EventSequence)
              .Take(100)
              .OrderBy(jobEvent => jobEvent.EventSequence)
              .ToArray();

            return ValueTask.FromResult<DashboardJobDetail?>(new DashboardJobDetail(
              this.CreateSummaryNoLock(task, DateTimeOffset.UtcNow),
              task.ClaimedAtUtc,
              task.ClaimedByNodeId,
              task.LeaseExpiresAtUtc,
              task.ScheduledFireAtUtc,
              recentEvents));
        }
    }

    public ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            return ValueTask.FromResult(this.CreateQueuePositionNoLock(taskId, DateTimeOffset.UtcNow));
        }
    }

    public async IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid taskId,
        DashboardEventQuery? query = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        query ??= new DashboardEventQuery();
        ValidateEventQuery(query);

        IReadOnlyList<DashboardJobEvent> events;
        lock (this._gate)
        {
            events = [.. this._events
              .Where(jobEvent => jobEvent.TaskId == taskId)
              .Where(jobEvent => query.AfterEventSequence is null || jobEvent.EventSequence > query.AfterEventSequence.Value)
              .OrderBy(jobEvent => jobEvent.EventSequence)
              .Take(query.Limit)];
        }

        foreach (var jobEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return jobEvent;
            await Task.Yield();
        }
    }

    public async ValueTask<DashboardJobEvent> AppendAsync(
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAppendRequest(request);

        DashboardJobEvent jobEvent;
        lock (this._gate)
        {
            if (!this._tasks.TryGetValue(request.TaskId, out var task))
            {
                throw new InvalidOperationException($"Task '{request.TaskId}' does not exist.");
            }

            jobEvent = this.AppendEventNoLock(
              task,
              request.Kind,
              request.AttemptNumber,
              request.Message,
              request.LogLevel,
              request.ProgressPercent,
              request.Fields);
        }

        await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        return jobEvent;
    }

    public ValueTask<int> CleanupAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "Dashboard event retention must be positive.");
        }

        lock (this._gate)
        {
            var threshold = DateTimeOffset.UtcNow.Subtract(retention);
            var removed = this._events.RemoveAll(jobEvent =>
              this._tasks.TryGetValue(jobEvent.TaskId, out var task)
              && GetTerminalAt(task) is { } terminalAt
              && terminalAt < threshold);

            return ValueTask.FromResult(removed);
        }
    }

    internal InMemoryTaskSnapshot? GetSnapshot(Guid taskId)
    {
        lock (this._gate)
        {
            return this._tasks.TryGetValue(taskId, out var task)
              ? CreateSnapshot(task)
              : null;
        }
    }

    private DashboardJobSummary CreateSummaryNoLock(
        InMemoryTaskRecord task,
        DateTimeOffset now)
      => new(
        task.TaskId,
        task.State,
        task.ServiceType,
        task.MethodName,
        task.Priority,
        task.EnqueueSequence,
        task.EnqueuedAtUtc,
        task.NotBeforeUtc,
        task.AttemptCount,
        task.MaxAttempts,
        [.. task.Tags],
        task.SourceScheduleKey,
        this.GetLatestProgressNoLock(task.TaskId),
        this.CreateQueuePositionNoLock(task.TaskId, now),
        task.CompletedAtUtc,
        task.FailedAtUtc,
        task.CanceledAtUtc);

    private DashboardProgressSnapshot? GetLatestProgressNoLock(Guid taskId)
      => this._events
        .Where(jobEvent => jobEvent.TaskId == taskId && jobEvent.Kind == DashboardJobEventKind.Progress)
        .OrderByDescending(jobEvent => jobEvent.EventSequence)
        .Select(jobEvent => new DashboardProgressSnapshot(jobEvent.ProgressPercent, jobEvent.Message, jobEvent.OccurredAtUtc))
        .FirstOrDefault();

    private DashboardQueuePosition CreateQueuePositionNoLock(Guid taskId, DateTimeOffset now)
    {
        if (!this._tasks.TryGetValue(taskId, out var task))
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.NotFound, Position: null, "Job was not found.");
        }

        if (task.State == TaskState.Canceled)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Canceled, Position: null, "Job was canceled.");
        }

        if (task.State is TaskState.Completed or TaskState.Failed)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Terminal, Position: null, "Job is terminal.");
        }

        if (task.State == TaskState.Claimed)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Claimed, Position: null, "Job is currently claimed.");
        }

        if (task.NotBeforeUtc is { } notBeforeUtc && notBeforeUtc > now)
        {
            return task.FailedAtUtc is null
              ? new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Delayed, Position: null, $"Job is delayed until {notBeforeUtc:O}.")
              : new DashboardQueuePosition(taskId, DashboardQueuePositionKind.RetryWaiting, Position: null, $"Job is waiting to retry until {notBeforeUtc:O}.");
        }

        if (!this.CanClaim(task))
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.BlockedByConcurrency, Position: null, "Job is blocked by concurrency group limits.");
        }

        var position = this._tasks.Values
          .Where(candidate => candidate.State == TaskState.Queued)
          .Where(candidate => candidate.NotBeforeUtc is null || candidate.NotBeforeUtc <= now)
          .Where(this.CanClaim)
          .OrderByDescending(candidate => candidate.Priority)
          .ThenBy(candidate => candidate.EnqueueSequence)
          .Select((candidate, index) => (candidate.TaskId, Position: index + 1L))
          .First(candidate => candidate.TaskId == taskId)
          .Position;

        return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Claimable, position, "Job is currently claimable.");
    }

    private DashboardJobEvent AppendEventNoLock(
        InMemoryTaskRecord task,
        DashboardJobEventKind kind,
        int attemptNumber,
        string? message = null,
        JobLogLevel? logLevel = null,
        double? progressPercent = null,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var jobEvent = new DashboardJobEvent(
          Guid.NewGuid(),
          task.TaskId,
          ++task.DashboardEventSequence,
          kind,
          DateTimeOffset.UtcNow,
          attemptNumber,
          logLevel,
          message,
          progressPercent,
          CopyFields(fields));

        this._events.Add(jobEvent);
        return jobEvent;
    }

    private async ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken)
      => await this._liveUpdatePublisher.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);

    private async ValueTask PublishAsync(
        IReadOnlyList<DashboardJobEvent> events,
        CancellationToken cancellationToken)
    {
        foreach (var jobEvent in events)
        {
            await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool MatchesQuery(InMemoryTaskRecord task, DashboardJobQuery query)
      => (query.TaskId is null || task.TaskId == query.TaskId.Value)
        && (query.State is null || task.State == query.State.Value)
        && (query.ServiceType is null || string.Equals(task.ServiceType, query.ServiceType, StringComparison.Ordinal))
        && (query.MethodName is null || string.Equals(task.MethodName, query.MethodName, StringComparison.Ordinal))
        && (query.Tag is null || task.Tags.Any(tag => string.Equals(tag.Name, query.Tag.Name, StringComparison.Ordinal)
          && string.Equals(tag.Value, query.Tag.Value, StringComparison.Ordinal)))
        && (query.SourceScheduleKey is null || string.Equals(task.SourceScheduleKey, query.SourceScheduleKey, StringComparison.Ordinal))
        && (query.EnqueuedFromUtc is null || task.EnqueuedAtUtc >= query.EnqueuedFromUtc.Value)
        && (query.EnqueuedToUtc is null || task.EnqueuedAtUtc <= query.EnqueuedToUtc.Value)
        && (query.TerminalFromUtc is null || GetTerminalAt(task) >= query.TerminalFromUtc.Value)
        && (query.TerminalToUtc is null || GetTerminalAt(task) <= query.TerminalToUtc.Value);

    private static DateTimeOffset? GetTerminalAt(InMemoryTaskRecord task)
      => task.State switch
      {
          TaskState.Completed => task.CompletedAtUtc,
          TaskState.Failed => task.FailedAtUtc,
          TaskState.Canceled => task.CanceledAtUtc,
          _ => null,
      };

    private static void ValidateJobQuery(DashboardJobQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, "Dashboard job query page size must be positive.");
        }

        if (query.Tag is not null)
        {
            SubmissionValidator.NormalizeJobTags([query.Tag]);
        }

        _ = DecodeContinuationToken(query.ContinuationToken);
    }

    private static void ValidateEventQuery(DashboardEventQuery query)
    {
        if (query.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Dashboard event query limit must be positive.");
        }
    }

    private static void ValidateAppendRequest(AppendDashboardJobEventRequest request)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Dashboard job event kind is not supported.");
        }

        if (request.AttemptNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.AttemptNumber, "Dashboard event attempt number cannot be negative.");
        }

        if (request.ProgressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.ProgressPercent, "Progress percent must be between 0 and 100.");
        }

        if (request.LogLevel is not null && !Enum.IsDefined(request.LogLevel.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.LogLevel, "Job log level is not supported.");
        }
    }

    private static long? DecodeContinuationToken(string? continuationToken)
    {
        if (continuationToken is null)
        {
            return null;
        }

        if (!long.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var enqueueSequence) || enqueueSequence <= 0)
        {
            throw new ArgumentException("Dashboard job continuation token is invalid.", nameof(continuationToken));
        }

        return enqueueSequence;
    }

    private static Dictionary<string, string>? CopyFields(IReadOnlyDictionary<string, string>? fields)
      => fields is null ? null : new Dictionary<string, string>(fields, StringComparer.Ordinal);

    private static InMemoryTaskRecord CreateTaskRecord(EnqueueTaskRequest request, long enqueueSequence)
      => new(
        request.TaskId,
        TaskState.Queued,
        request.Priority,
        enqueueSequence,
        request.EnqueuedAtUtc,
        request.ServiceType,
        request.MethodName,
        [.. request.MethodParameterTypes],
        ClonePayload(request.SerializedArguments),
        [.. request.ConcurrencyGroupKeys.Distinct(StringComparer.Ordinal)],
        request.NotBeforeUtc?.ToUniversalTime(),
        request.MaxAttempts,
        request.RetryBackoffKind,
        request.RetryBaseDelay,
        request.RetryMaxDelay,
        request.SourceScheduleKey,
        request.ScheduledFireAtUtc?.ToUniversalTime(),
        SubmissionValidator.NormalizeJobTags(request.Tags));

    private static ClaimedTask CreateClaimedTask(InMemoryTaskRecord task)
      => new(
        task.TaskId,
        task.EnqueueSequence,
        task.Priority,
        task.ServiceType,
        task.MethodName,
        [.. task.MethodParameterTypes],
        ClonePayload(task.SerializedArguments),
        [.. task.ConcurrencyGroupKeys],
        task.AttemptCount,
        task.MaxAttempts,
        task.LeaseToken.GetValueOrDefault(),
        task.LeaseExpiresAtUtc.GetValueOrDefault(),
        task.RetryBackoffKind,
        task.RetryBaseDelay,
        task.RetryMaxDelay,
        task.SourceScheduleKey,
        task.ScheduledFireAtUtc);

    private static InMemoryTaskSnapshot CreateSnapshot(InMemoryTaskRecord task)
      => new(
        task.TaskId,
        task.State,
        task.Priority,
        task.EnqueueSequence,
        task.EnqueuedAtUtc,
        task.ServiceType,
        task.MethodName,
        [.. task.MethodParameterTypes],
        ClonePayload(task.SerializedArguments),
        [.. task.ConcurrencyGroupKeys],
        task.NotBeforeUtc,
        task.AttemptCount,
        task.MaxAttempts,
        task.RetryBackoffKind,
        task.RetryBaseDelay,
        task.RetryMaxDelay,
        task.LeaseToken,
        task.LeaseExpiresAtUtc,
        task.LastHeartbeatAtUtc,
        task.ClaimedByNodeId,
        task.ClaimedAtUtc,
        task.CompletedAtUtc,
        task.FailedAtUtc,
        task.Failure,
        task.CanceledAtUtc,
        task.SourceScheduleKey,
        task.ScheduledFireAtUtc,
        [.. task.Tags]);

    private static RecurringScheduleInfo CreateScheduleInfo(InMemoryRecurringScheduleRecord schedule)
      => new(
        schedule.ScheduleKey,
        schedule.CronExpression,
        schedule.IsPaused,
        schedule.OverlapMode,
        schedule.Priority,
        [.. schedule.ConcurrencyGroupKeys],
        schedule.RetryPolicy,
        schedule.NextFireAtUtc);

    private static void ApplyFailedAttempt(InMemoryTaskRecord task, DateTimeOffset failedAtUtc, TaskFailureInfo failure)
    {
        task.FailedAtUtc = failedAtUtc;
        task.Failure = failure;

        if (task.AttemptCount < task.MaxAttempts)
        {
            task.State = TaskState.Queued;
            task.NotBeforeUtc = failedAtUtc.Add(CalculateBackoff(task));
            ClearClaim(task);
            return;
        }

        task.State = TaskState.Failed;
    }

    private static TimeSpan CalculateBackoff(InMemoryTaskRecord task)
    {
        if (task.RetryBackoffKind is null || task.RetryBaseDelay is null)
        {
            return TimeSpan.Zero;
        }

        if (task.RetryBackoffKind == RetryBackoffKind.Fixed)
        {
            return task.RetryBaseDelay.Value;
        }

        var multiplier = Math.Pow(2, task.AttemptCount - 1);
        var ticks = task.RetryBaseDelay.Value.Ticks * multiplier;
        var delay = TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, ticks));

        return task.RetryMaxDelay is { } maxDelay && delay > maxDelay ? maxDelay : delay;
    }

    private static void ClearClaim(InMemoryTaskRecord task)
    {
        task.ClaimedByNodeId = null;
        task.ClaimedAtUtc = null;
        task.LeaseToken = null;
        task.LeaseExpiresAtUtc = null;
        task.LastHeartbeatAtUtc = null;
    }

    private bool CanClaim(InMemoryTaskRecord task)
    {
        foreach (var groupKey in task.ConcurrencyGroupKeys)
        {
            var limit = this._concurrencyLimits.GetValueOrDefault(groupKey, 1);
            var occupancy = this._tasks.Values.Count(candidate =>
                    candidate.State == TaskState.Claimed
                    && candidate.ConcurrencyGroupKeys.Contains(groupKey, StringComparer.Ordinal));

            if (occupancy >= limit)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetCurrentClaim(
        Guid taskId,
        string nodeId,
        Guid leaseToken,
        DateTimeOffset observedAtUtc,
        out InMemoryTaskRecord task)
    {
        task = null!;

        if (!this._tasks.TryGetValue(taskId, out var candidate)
            || candidate.State != TaskState.Claimed
            || !string.Equals(candidate.ClaimedByNodeId, nodeId, StringComparison.Ordinal)
            || candidate.LeaseToken != leaseToken
            || candidate.LeaseExpiresAtUtc <= observedAtUtc)
        {
            return false;
        }

        task = candidate;
        return true;
    }

    private static bool ScheduleDefinitionEquals(InMemoryRecurringScheduleRecord schedule, UpsertRecurringScheduleRequest request)
      => string.Equals(schedule.CronExpression, request.CronExpression, StringComparison.Ordinal)
        && string.Equals(schedule.ServiceType, request.ServiceType, StringComparison.Ordinal)
        && string.Equals(schedule.MethodName, request.MethodName, StringComparison.Ordinal)
        && schedule.MethodParameterTypes.SequenceEqual(request.MethodParameterTypes, StringComparer.Ordinal)
        && PayloadEquals(schedule.SerializedArguments, request.SerializedArguments)
        && schedule.Priority == request.Priority
        && schedule.ConcurrencyGroupKeys.SequenceEqual(request.ConcurrencyGroupKeys, StringComparer.Ordinal)
        && schedule.RetryPolicy == request.RetryPolicy
        && schedule.OverlapMode == request.OverlapMode;

    private static bool PayloadEquals(SerializedTaskPayload left, SerializedTaskPayload right)
      => string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
        && left.Data.SequenceEqual(right.Data);

    private static void ValidateEnqueueRequest(EnqueueTaskRequest request)
    {
        if (request.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.MaxAttempts, "Max attempts must be greater than or equal to 1.");
        }

        if (request.MaxAttempts > 1)
        {
            if (request.RetryBackoffKind is null)
            {
                throw new ArgumentException("Retry backoff kind is required when max attempts is greater than 1.", nameof(request));
            }

            if (request.RetryBaseDelay is null || request.RetryBaseDelay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Retry base delay must be positive when max attempts is greater than 1.", nameof(request));
            }

            if (request.RetryMaxDelay is { } maxDelay && maxDelay < request.RetryBaseDelay)
            {
                throw new ArgumentException("Retry max delay must be greater than or equal to the base delay.", nameof(request));
            }
        }

        if (request.RetryBackoffKind is not null && !Enum.IsDefined(request.RetryBackoffKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.RetryBackoffKind, "Retry backoff kind is not supported.");
        }

        foreach (var groupKey in request.ConcurrencyGroupKeys)
        {
            SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);
        }
    }

    private static void ValidateRecurringScheduleRequest(UpsertRecurringScheduleRequest request)
    {
        SubmissionValidator.ValidateScheduleKey(request.ScheduleKey);
        CronSchedule.Validate(request.CronExpression);
        SubmissionValidator.ValidateRetryPolicy(request.RetryPolicy);

        if (!Enum.IsDefined(request.OverlapMode))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.OverlapMode, "Recurring overlap mode is not supported.");
        }

        foreach (var groupKey in request.ConcurrencyGroupKeys)
        {
            SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);
        }
    }

    private static SerializedTaskPayload ClonePayload(SerializedTaskPayload payload)
      => new(payload.ContentType, [.. payload.Data]);
}

internal sealed class InMemoryNoOpDashboardLiveUpdatePublisher : IDashboardLiveUpdatePublisher
{
    public ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
