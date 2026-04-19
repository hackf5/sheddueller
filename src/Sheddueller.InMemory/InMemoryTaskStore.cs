namespace Sheddueller;

using Sheddueller.Enqueueing;
using Sheddueller.Scheduling;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class InMemoryTaskStore : ITaskStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, InMemoryTaskRecord> _tasks = [];
    private readonly Dictionary<string, InMemoryRecurringScheduleRecord> _recurringSchedules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _concurrencyLimits = new(StringComparer.Ordinal);
    private long _nextEnqueueSequence;

    public ValueTask<EnqueueTaskResult> EnqueueAsync(
      EnqueueTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnqueueRequest(request);

        lock (this._gate)
        {
            if (this._tasks.ContainsKey(request.TaskId))
            {
                throw new InvalidOperationException($"Task '{request.TaskId}' already exists.");
            }

            var enqueueSequence = ++this._nextEnqueueSequence;
            this._tasks.Add(request.TaskId, CreateTaskRecord(request, enqueueSequence));

            return ValueTask.FromResult(new EnqueueTaskResult(request.TaskId, enqueueSequence));
        }
    }

    public ValueTask<ClaimTaskResult> TryClaimNextAsync(
      ClaimTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.LeaseExpiresAtUtc <= request.ClaimedAtUtc)
        {
            throw new ArgumentException("Lease expiry must be after the claimed timestamp.", nameof(request));
        }

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

                return ValueTask.FromResult<ClaimTaskResult>(new ClaimTaskResult.Claimed(CreateClaimedTask(task)));
            }

            return ValueTask.FromResult<ClaimTaskResult>(new ClaimTaskResult.NoTaskAvailable());
        }
    }

    public ValueTask<bool> MarkCompletedAsync(
      CompleteTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.CompletedAtUtc, out var task))
            {
                return ValueTask.FromResult(false);
            }

            task.State = TaskState.Completed;
            task.CompletedAtUtc = request.CompletedAtUtc;

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> MarkFailedAsync(
      FailTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.FailedAtUtc, out var task))
            {
                return ValueTask.FromResult(false);
            }

            ApplyFailedAttempt(task, request.FailedAtUtc, request.Failure);

            return ValueTask.FromResult(true);
        }
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

    public ValueTask<bool> ReleaseTaskAsync(
      ReleaseTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            if (!this.TryGetCurrentClaim(request.TaskId, request.NodeId, request.LeaseToken, request.ReleasedAtUtc, out var task))
            {
                return ValueTask.FromResult(false);
            }

            task.State = TaskState.Queued;
            task.AttemptCount = Math.Max(0, task.AttemptCount - 1);
            task.NotBeforeUtc = null;
            ClearClaim(task);

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> RecoverExpiredLeasesAsync(
      RecoverExpiredLeasesRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            var recovered = 0;
            foreach (var task in this._tasks.Values
              .Where(task => task.State == TaskState.Claimed && task.LeaseExpiresAtUtc <= request.RecoveredAtUtc)
              .ToArray())
            {
                ApplyFailedAttempt(task, request.RecoveredAtUtc, new TaskFailureInfo(
                  "Sheddueller.LeaseExpired",
                  "The task lease expired before the owning node renewed it.",
                  null));
                recovered++;
            }

            return ValueTask.FromResult(recovered);
        }
    }

    public ValueTask<bool> CancelAsync(
      CancelTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this._gate)
        {
            if (!this._tasks.TryGetValue(request.TaskId, out var task) || task.State != TaskState.Queued)
            {
                return ValueTask.FromResult(false);
            }

            task.State = TaskState.Canceled;
            task.CanceledAtUtc = request.CanceledAtUtc;

            return ValueTask.FromResult(true);
        }
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

    public ValueTask<int> MaterializeDueRecurringSchedulesAsync(
      MaterializeDueRecurringSchedulesRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateRetryPolicy(request.DefaultRetryPolicy);

        lock (this._gate)
        {
            var materialized = 0;

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
                    this._tasks.Add(taskId, new InMemoryTaskRecord(
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
                      notBeforeUtc: null,
                      maxAttempts,
                      retryBackoffKind,
                      retryBaseDelay,
                      retryMaxDelay,
                      schedule.ScheduleKey,
                      scheduledFireAtUtc));
                    materialized++;
                }

                schedule.NextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(schedule.CronExpression, request.MaterializedAtUtc);
            }

            return ValueTask.FromResult(materialized);
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
        request.ScheduledFireAtUtc?.ToUniversalTime());

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
        task.ScheduledFireAtUtc);

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
