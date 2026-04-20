namespace Sheddueller;

using System.Globalization;
using System.Runtime.CompilerServices;

using Sheddueller.Dashboard;
using Sheddueller.Enqueueing;
using Sheddueller.Scheduling;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class InMemoryJobStore(IDashboardLiveUpdatePublisher liveUpdatePublisher)
    : IJobStore, IDashboardJobReader, IDashboardEventSink, IDashboardEventRetentionStore
{
    private readonly Lock _gate = new();
    private readonly IDashboardLiveUpdatePublisher _liveUpdatePublisher = liveUpdatePublisher;
    private readonly Dictionary<Guid, InMemoryJobRecord> _jobs = [];
    private readonly List<DashboardJobEvent> _events = [];
    private readonly Dictionary<string, InMemoryRecurringScheduleRecord> _recurringSchedules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _concurrencyLimits = new(StringComparer.Ordinal);
    private long _nextEnqueueSequence;

    public InMemoryJobStore()
      : this(new InMemoryNoOpDashboardLiveUpdatePublisher())
    {
    }

    public async ValueTask<EnqueueJobResult> EnqueueAsync(
      EnqueueJobRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnqueueRequest(request);

        DashboardJobEvent jobEvent;
        EnqueueJobResult result;
        lock (this._gate)
        {
            if (this._jobs.ContainsKey(request.JobId))
            {
                throw new InvalidOperationException($"Job '{request.JobId}' already exists.");
            }

            var enqueueSequence = ++this._nextEnqueueSequence;
            var job = CreateTaskRecord(request, enqueueSequence);
            this._jobs.Add(request.JobId, job);
            jobEvent = this.AppendEventNoLock(
              job,
              DashboardJobEventKind.Lifecycle,
              attemptNumber: 0,
              message: "Queued");
            result = new EnqueueJobResult(request.JobId, enqueueSequence);
        }

        await this.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ClaimJobResult> TryClaimNextAsync(
      ClaimJobRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.LeaseExpiresAtUtc <= request.ClaimedAtUtc)
        {
            throw new ArgumentException("Lease expiry must be after the claimed timestamp.", nameof(request));
        }

        DashboardJobEvent? jobEvent = null;
        ClaimJobResult result = new ClaimJobResult.NoJobAvailable();
        lock (this._gate)
        {
            foreach (var job in this._jobs.Values
              .Where(job => job.State == JobState.Queued)
              .Where(job => job.NotBeforeUtc is null || job.NotBeforeUtc <= request.ClaimedAtUtc)
              .OrderByDescending(job => job.Priority)
              .ThenBy(job => job.EnqueueSequence))
            {
                if (!this.CanClaim(job))
                {
                    continue;
                }

                job.State = JobState.Claimed;
                job.AttemptCount++;
                job.ClaimedByNodeId = request.NodeId;
                job.ClaimedAtUtc = request.ClaimedAtUtc;
                job.LeaseToken = Guid.NewGuid();
                job.LeaseExpiresAtUtc = request.LeaseExpiresAtUtc;
                job.LastHeartbeatAtUtc = null;

                jobEvent = this.AppendEventNoLock(
                  job,
                  DashboardJobEventKind.AttemptStarted,
                  job.AttemptCount,
                  "Attempt started");
                result = new ClaimJobResult.Claimed(CreateClaimedJob(job));
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
      CompleteJobRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DashboardJobEvent> events;
        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.JobId, request.NodeId, request.LeaseToken, request.CompletedAtUtc, out var job))
            {
                return false;
            }

            job.State = JobState.Completed;
            job.CompletedAtUtc = request.CompletedAtUtc;
            events =
            [
                this.AppendEventNoLock(job, DashboardJobEventKind.AttemptCompleted, job.AttemptCount, "Attempt completed"),
                this.AppendEventNoLock(job, DashboardJobEventKind.Lifecycle, job.AttemptCount, "Completed"),
            ];
        }

        await this.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> MarkFailedAsync(
      FailJobRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DashboardJobEvent> events;
        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.JobId, request.NodeId, request.LeaseToken, request.FailedAtUtc, out var job))
            {
                return false;
            }

            ApplyFailedAttempt(job, request.FailedAtUtc, request.Failure);
            events =
            [
                this.AppendEventNoLock(job, DashboardJobEventKind.AttemptFailed, job.AttemptCount, request.Failure.Message),
                this.AppendEventNoLock(job, DashboardJobEventKind.Lifecycle, job.AttemptCount, job.State == JobState.Failed ? "Failed" : "Retry scheduled"),
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
            if (!this.TryGetCurrentClaim(request.JobId, request.NodeId, request.LeaseToken, request.HeartbeatAtUtc, out var job))
            {
                return ValueTask.FromResult(false);
            }

            job.LastHeartbeatAtUtc = request.HeartbeatAtUtc;
            job.LeaseExpiresAtUtc = request.LeaseExpiresAtUtc;

            return ValueTask.FromResult(true);
        }
    }

    public async ValueTask<bool> ReleaseJobAsync(
      ReleaseJobRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        DashboardJobEvent jobEvent;
        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.JobId, request.NodeId, request.LeaseToken, request.ReleasedAtUtc, out var job))
            {
                return false;
            }

            var attemptNumber = job.AttemptCount;
            job.State = JobState.Queued;
            job.AttemptCount = Math.Max(0, job.AttemptCount - 1);
            job.NotBeforeUtc = null;
            ClearClaim(job);
            jobEvent = this.AppendEventNoLock(job, DashboardJobEventKind.Lifecycle, attemptNumber, "Released");
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
            foreach (var job in this._jobs.Values
              .Where(job => job.State == JobState.Claimed && job.LeaseExpiresAtUtc <= request.RecoveredAtUtc)
              .ToArray())
            {
                ApplyFailedAttempt(job, request.RecoveredAtUtc, new JobFailureInfo(
                  "Sheddueller.LeaseExpired",
                  "The job lease expired before the owning node renewed it.",
                  null));
                events.Add(this.AppendEventNoLock(job, DashboardJobEventKind.AttemptFailed, job.AttemptCount, "The job lease expired before the owning node renewed it."));
                events.Add(this.AppendEventNoLock(job, DashboardJobEventKind.Lifecycle, job.AttemptCount, job.State == JobState.Failed ? "Failed" : "Retry scheduled"));
                recovered++;
            }
        }

        await this.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        return recovered;
    }

    public async ValueTask<bool> CancelAsync(
      CancelJobRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        DashboardJobEvent jobEvent;
        lock (this._gate)
        {
            if (!this._jobs.TryGetValue(request.JobId, out var job) || job.State != JobState.Queued)
            {
                return false;
            }

            job.State = JobState.Canceled;
            job.CanceledAtUtc = request.CanceledAtUtc;
            jobEvent = this.AppendEventNoLock(job, DashboardJobEventKind.Lifecycle, job.AttemptCount, "Canceled");
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
                  || !this._jobs.Values.Any(job =>
                    string.Equals(job.SourceScheduleKey, schedule.ScheduleKey, StringComparison.Ordinal)
                    && job.State is JobState.Queued or JobState.Claimed);

                if (canMaterialize)
                {
                    var (maxAttempts, retryBackoffKind, retryBaseDelay, retryMaxDelay) = SubmissionValidator.NormalizeRetryPolicy(
                      schedule.RetryPolicy ?? request.DefaultRetryPolicy);
                    var jobId = Guid.NewGuid();
                    var enqueueSequence = ++this._nextEnqueueSequence;
                    var job = new InMemoryJobRecord(
                      jobId,
                      JobState.Queued,
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
                    this._jobs.Add(jobId, job);
                    events.Add(this.AppendEventNoLock(job, DashboardJobEventKind.Lifecycle, attemptNumber: 0, "Queued"));
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
            var summaries = this._jobs.Values
              .Select(job => this.CreateSummaryNoLock(job, now))
              .ToArray();
            var stateCounts = Enum.GetValues<JobState>()
              .ToDictionary(state => state, state => summaries.Count(summary => summary.State == state));

            return ValueTask.FromResult(new DashboardJobOverview(
              stateCounts,
              [.. summaries
                .Where(summary => summary.State == JobState.Claimed)
                .OrderByDescending(summary => summary.EnqueuedAtUtc)
                .Take(10)],
              [.. summaries
                .Where(summary => summary.State == JobState.Failed)
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
            var matched = this._jobs.Values
              .Where(job => MatchesQuery(job, query))
              .Where(job => afterSequence is null || job.EnqueueSequence < afterSequence.Value)
              .OrderByDescending(job => job.EnqueueSequence)
              .Take(query.PageSize + 1)
              .ToArray();
            var pageItems = matched.Take(query.PageSize).ToArray();
            var continuationToken = matched.Length > query.PageSize
              ? pageItems[^1].EnqueueSequence.ToString(CultureInfo.InvariantCulture)
              : null;

            return ValueTask.FromResult(new DashboardJobPage(
              [.. pageItems.Select(job => this.CreateSummaryNoLock(job, now))],
              continuationToken));
        }
    }

    public ValueTask<DashboardJobDetail?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            if (!this._jobs.TryGetValue(jobId, out var job))
            {
                return ValueTask.FromResult<DashboardJobDetail?>(null);
            }

            var recentEvents = this._events
              .Where(jobEvent => jobEvent.JobId == jobId)
              .OrderByDescending(jobEvent => jobEvent.EventSequence)
              .Take(100)
              .OrderBy(jobEvent => jobEvent.EventSequence)
              .ToArray();

            return ValueTask.FromResult<DashboardJobDetail?>(new DashboardJobDetail(
              this.CreateSummaryNoLock(job, DateTimeOffset.UtcNow),
              job.ClaimedAtUtc,
              job.ClaimedByNodeId,
              job.LeaseExpiresAtUtc,
              job.ScheduledFireAtUtc,
              recentEvents));
        }
    }

    public ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            return ValueTask.FromResult(this.CreateQueuePositionNoLock(jobId, DateTimeOffset.UtcNow));
        }
    }

    public async IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        Guid jobId,
        DashboardEventQuery? query = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        query ??= new DashboardEventQuery();
        ValidateEventQuery(query);

        IReadOnlyList<DashboardJobEvent> events;
        lock (this._gate)
        {
            events = [.. this._events
              .Where(jobEvent => jobEvent.JobId == jobId)
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
            if (!this._jobs.TryGetValue(request.JobId, out var job))
            {
                throw new InvalidOperationException($"Job '{request.JobId}' does not exist.");
            }

            jobEvent = this.AppendEventNoLock(
              job,
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
              this._jobs.TryGetValue(jobEvent.JobId, out var job)
              && GetTerminalAt(job) is { } terminalAt
              && terminalAt < threshold);

            return ValueTask.FromResult(removed);
        }
    }

    internal InMemoryJobSnapshot? GetSnapshot(Guid jobId)
    {
        lock (this._gate)
        {
            return this._jobs.TryGetValue(jobId, out var job)
              ? CreateSnapshot(job)
              : null;
        }
    }

    private DashboardJobSummary CreateSummaryNoLock(
        InMemoryJobRecord job,
        DateTimeOffset now)
      => new(
        job.JobId,
        job.State,
        job.ServiceType,
        job.MethodName,
        job.Priority,
        job.EnqueueSequence,
        job.EnqueuedAtUtc,
        job.NotBeforeUtc,
        job.AttemptCount,
        job.MaxAttempts,
        [.. job.Tags],
        job.SourceScheduleKey,
        this.GetLatestProgressNoLock(job.JobId),
        this.CreateQueuePositionNoLock(job.JobId, now),
        job.CompletedAtUtc,
        job.FailedAtUtc,
        job.CanceledAtUtc);

    private DashboardProgressSnapshot? GetLatestProgressNoLock(Guid jobId)
      => this._events
        .Where(jobEvent => jobEvent.JobId == jobId && jobEvent.Kind == DashboardJobEventKind.Progress)
        .OrderByDescending(jobEvent => jobEvent.EventSequence)
        .Select(jobEvent => new DashboardProgressSnapshot(jobEvent.ProgressPercent, jobEvent.Message, jobEvent.OccurredAtUtc))
        .FirstOrDefault();

    private DashboardQueuePosition CreateQueuePositionNoLock(Guid jobId, DateTimeOffset now)
    {
        if (!this._jobs.TryGetValue(jobId, out var job))
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.NotFound, Position: null, "Job was not found.");
        }

        if (job.State == JobState.Canceled)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Canceled, Position: null, "Job was canceled.");
        }

        if (job.State is JobState.Completed or JobState.Failed)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Terminal, Position: null, "Job is terminal.");
        }

        if (job.State == JobState.Claimed)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Claimed, Position: null, "Job is currently claimed.");
        }

        if (job.NotBeforeUtc is { } notBeforeUtc && notBeforeUtc > now)
        {
            return job.FailedAtUtc is null
              ? new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Delayed, Position: null, $"Job is delayed until {notBeforeUtc:O}.")
              : new DashboardQueuePosition(jobId, DashboardQueuePositionKind.RetryWaiting, Position: null, $"Job is waiting to retry until {notBeforeUtc:O}.");
        }

        if (!this.CanClaim(job))
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.BlockedByConcurrency, Position: null, "Job is blocked by concurrency group limits.");
        }

        var position = this._jobs.Values
          .Where(candidate => candidate.State == JobState.Queued)
          .Where(candidate => candidate.NotBeforeUtc is null || candidate.NotBeforeUtc <= now)
          .Where(this.CanClaim)
          .OrderByDescending(candidate => candidate.Priority)
          .ThenBy(candidate => candidate.EnqueueSequence)
          .Select((candidate, index) => (candidate.JobId, Position: index + 1L))
          .First(candidate => candidate.JobId == jobId)
          .Position;

        return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Claimable, position, "Job is currently claimable.");
    }

    private DashboardJobEvent AppendEventNoLock(
        InMemoryJobRecord job,
        DashboardJobEventKind kind,
        int attemptNumber,
        string? message = null,
        JobLogLevel? logLevel = null,
        double? progressPercent = null,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var jobEvent = new DashboardJobEvent(
          Guid.NewGuid(),
          job.JobId,
          ++job.DashboardEventSequence,
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

    private static bool MatchesQuery(InMemoryJobRecord job, DashboardJobQuery query)
      => (query.JobId is null || job.JobId == query.JobId.Value)
        && (query.State is null || job.State == query.State.Value)
        && (query.ServiceType is null || string.Equals(job.ServiceType, query.ServiceType, StringComparison.Ordinal))
        && (query.MethodName is null || string.Equals(job.MethodName, query.MethodName, StringComparison.Ordinal))
        && (query.Tag is null || job.Tags.Any(tag => string.Equals(tag.Name, query.Tag.Name, StringComparison.Ordinal)
          && string.Equals(tag.Value, query.Tag.Value, StringComparison.Ordinal)))
        && (query.SourceScheduleKey is null || string.Equals(job.SourceScheduleKey, query.SourceScheduleKey, StringComparison.Ordinal))
        && (query.EnqueuedFromUtc is null || job.EnqueuedAtUtc >= query.EnqueuedFromUtc.Value)
        && (query.EnqueuedToUtc is null || job.EnqueuedAtUtc <= query.EnqueuedToUtc.Value)
        && (query.TerminalFromUtc is null || GetTerminalAt(job) >= query.TerminalFromUtc.Value)
        && (query.TerminalToUtc is null || GetTerminalAt(job) <= query.TerminalToUtc.Value);

    private static DateTimeOffset? GetTerminalAt(InMemoryJobRecord job)
      => job.State switch
      {
          JobState.Completed => job.CompletedAtUtc,
          JobState.Failed => job.FailedAtUtc,
          JobState.Canceled => job.CanceledAtUtc,
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

    private static InMemoryJobRecord CreateTaskRecord(EnqueueJobRequest request, long enqueueSequence)
      => new(
        request.JobId,
        JobState.Queued,
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

    private static ClaimedJob CreateClaimedJob(InMemoryJobRecord job)
      => new(
        job.JobId,
        job.EnqueueSequence,
        job.Priority,
        job.ServiceType,
        job.MethodName,
        [.. job.MethodParameterTypes],
        ClonePayload(job.SerializedArguments),
        [.. job.ConcurrencyGroupKeys],
        job.AttemptCount,
        job.MaxAttempts,
        job.LeaseToken.GetValueOrDefault(),
        job.LeaseExpiresAtUtc.GetValueOrDefault(),
        job.RetryBackoffKind,
        job.RetryBaseDelay,
        job.RetryMaxDelay,
        job.SourceScheduleKey,
        job.ScheduledFireAtUtc);

    private static InMemoryJobSnapshot CreateSnapshot(InMemoryJobRecord job)
      => new(
        job.JobId,
        job.State,
        job.Priority,
        job.EnqueueSequence,
        job.EnqueuedAtUtc,
        job.ServiceType,
        job.MethodName,
        [.. job.MethodParameterTypes],
        ClonePayload(job.SerializedArguments),
        [.. job.ConcurrencyGroupKeys],
        job.NotBeforeUtc,
        job.AttemptCount,
        job.MaxAttempts,
        job.RetryBackoffKind,
        job.RetryBaseDelay,
        job.RetryMaxDelay,
        job.LeaseToken,
        job.LeaseExpiresAtUtc,
        job.LastHeartbeatAtUtc,
        job.ClaimedByNodeId,
        job.ClaimedAtUtc,
        job.CompletedAtUtc,
        job.FailedAtUtc,
        job.Failure,
        job.CanceledAtUtc,
        job.SourceScheduleKey,
        job.ScheduledFireAtUtc,
        [.. job.Tags]);

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

    private static void ApplyFailedAttempt(InMemoryJobRecord job, DateTimeOffset failedAtUtc, JobFailureInfo failure)
    {
        job.FailedAtUtc = failedAtUtc;
        job.Failure = failure;

        if (job.AttemptCount < job.MaxAttempts)
        {
            job.State = JobState.Queued;
            job.NotBeforeUtc = failedAtUtc.Add(CalculateBackoff(job));
            ClearClaim(job);
            return;
        }

        job.State = JobState.Failed;
    }

    private static TimeSpan CalculateBackoff(InMemoryJobRecord job)
    {
        if (job.RetryBackoffKind is null || job.RetryBaseDelay is null)
        {
            return TimeSpan.Zero;
        }

        if (job.RetryBackoffKind == RetryBackoffKind.Fixed)
        {
            return job.RetryBaseDelay.Value;
        }

        var multiplier = Math.Pow(2, job.AttemptCount - 1);
        var ticks = job.RetryBaseDelay.Value.Ticks * multiplier;
        var delay = TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, ticks));

        return job.RetryMaxDelay is { } maxDelay && delay > maxDelay ? maxDelay : delay;
    }

    private static void ClearClaim(InMemoryJobRecord job)
    {
        job.ClaimedByNodeId = null;
        job.ClaimedAtUtc = null;
        job.LeaseToken = null;
        job.LeaseExpiresAtUtc = null;
        job.LastHeartbeatAtUtc = null;
    }

    private bool CanClaim(InMemoryJobRecord job)
    {
        foreach (var groupKey in job.ConcurrencyGroupKeys)
        {
            var limit = this._concurrencyLimits.GetValueOrDefault(groupKey, 1);
            var occupancy = this._jobs.Values.Count(candidate =>
                    candidate.State == JobState.Claimed
                    && candidate.ConcurrencyGroupKeys.Contains(groupKey, StringComparer.Ordinal));

            if (occupancy >= limit)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetCurrentClaim(
        Guid jobId,
        string nodeId,
        Guid leaseToken,
        DateTimeOffset observedAtUtc,
        out InMemoryJobRecord job)
    {
        job = null!;

        if (!this._jobs.TryGetValue(jobId, out var candidate)
            || candidate.State != JobState.Claimed
            || !string.Equals(candidate.ClaimedByNodeId, nodeId, StringComparison.Ordinal)
            || candidate.LeaseToken != leaseToken
            || candidate.LeaseExpiresAtUtc <= observedAtUtc)
        {
            return false;
        }

        job = candidate;
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

    private static bool PayloadEquals(SerializedJobPayload left, SerializedJobPayload right)
      => string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
        && left.Data.SequenceEqual(right.Data);

    private static void ValidateEnqueueRequest(EnqueueJobRequest request)
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

    private static SerializedJobPayload ClonePayload(SerializedJobPayload payload)
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
