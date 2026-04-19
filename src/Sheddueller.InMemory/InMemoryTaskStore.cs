namespace Sheddueller;

internal sealed class InMemoryTaskStore : ITaskStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, InMemoryTaskRecord> tasks = new();
    private readonly Dictionary<string, int> concurrencyLimits = new(StringComparer.Ordinal);
    private long nextEnqueueSequence;

    public ValueTask<EnqueueTaskResult> EnqueueAsync(
      EnqueueTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (tasks.ContainsKey(request.TaskId))
            {
                throw new InvalidOperationException($"Task '{request.TaskId}' already exists.");
            }

            foreach (var groupKey in request.ConcurrencyGroupKeys)
            {
                SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);
            }

            var enqueueSequence = ++nextEnqueueSequence;
            var record = new InMemoryTaskRecord(
              request.TaskId,
              TaskState.Queued,
              request.Priority,
              enqueueSequence,
              request.EnqueuedAtUtc,
              request.ServiceType,
              request.MethodName,
              request.MethodParameterTypes.ToArray(),
              ClonePayload(request.SerializedArguments),
              request.ConcurrencyGroupKeys.Distinct(StringComparer.Ordinal).ToArray());

            tasks.Add(request.TaskId, record);

            return ValueTask.FromResult(new EnqueueTaskResult(request.TaskId, enqueueSequence));
        }
    }

    public ValueTask<ClaimTaskResult> TryClaimNextAsync(
      ClaimTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            foreach (var task in tasks.Values
              .Where(task => task.State == TaskState.Queued)
              .OrderByDescending(task => task.Priority)
              .ThenBy(task => task.EnqueueSequence))
            {
                if (!CanClaim(task))
                {
                    continue;
                }

                task.State = TaskState.Claimed;
                task.ClaimedByNodeId = request.NodeId;
                task.ClaimedAtUtc = request.ClaimedAtUtc;

                return ValueTask.FromResult<ClaimTaskResult>(new ClaimTaskResult.Claimed(new ClaimedTask(
                  task.TaskId,
                  task.EnqueueSequence,
                  task.Priority,
                  task.ServiceType,
                  task.MethodName,
                  task.MethodParameterTypes.ToArray(),
                  ClonePayload(task.SerializedArguments),
                  task.ConcurrencyGroupKeys.ToArray())));
            }

            return ValueTask.FromResult<ClaimTaskResult>(new ClaimTaskResult.NoTaskAvailable());
        }
    }

    public ValueTask MarkCompletedAsync(
      CompleteTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var task = GetClaimedTaskForNode(request.TaskId, request.NodeId);
            task.State = TaskState.Completed;
            task.CompletedAtUtc = request.CompletedAtUtc;

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask MarkFailedAsync(
      FailTaskRequest request,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var task = GetClaimedTaskForNode(request.TaskId, request.NodeId);
            task.State = TaskState.Failed;
            task.FailedAtUtc = request.FailedAtUtc;
            task.Failure = request.Failure;

            return ValueTask.CompletedTask;
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

        lock (gate)
        {
            concurrencyLimits[request.GroupKey] = request.Limit;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
      string groupKey,
      CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);

        lock (gate)
        {
            return ValueTask.FromResult(concurrencyLimits.TryGetValue(groupKey, out var limit) ? (int?)limit : null);
        }
    }

    internal InMemoryTaskSnapshot? GetSnapshot(Guid taskId)
    {
        lock (gate)
        {
            return tasks.TryGetValue(taskId, out var task)
              ? new InMemoryTaskSnapshot(
                task.TaskId,
                task.State,
                task.Priority,
                task.EnqueueSequence,
                task.EnqueuedAtUtc,
                task.ServiceType,
                task.MethodName,
                task.MethodParameterTypes.ToArray(),
                ClonePayload(task.SerializedArguments),
                task.ConcurrencyGroupKeys.ToArray(),
                task.ClaimedByNodeId,
                task.ClaimedAtUtc,
                task.CompletedAtUtc,
                task.FailedAtUtc,
                task.Failure)
              : null;
        }
    }

    private bool CanClaim(InMemoryTaskRecord task)
    {
        foreach (var groupKey in task.ConcurrencyGroupKeys)
        {
            var limit = concurrencyLimits.GetValueOrDefault(groupKey, 1);
            var occupancy = tasks.Values.Count(candidate =>
              candidate.State == TaskState.Claimed
              && candidate.ConcurrencyGroupKeys.Contains(groupKey, StringComparer.Ordinal));

            if (occupancy >= limit)
            {
                return false;
            }
        }

        return true;
    }

    private InMemoryTaskRecord GetClaimedTaskForNode(Guid taskId, string nodeId)
    {
        if (!tasks.TryGetValue(taskId, out var task))
        {
            throw new InvalidOperationException($"Task '{taskId}' does not exist.");
        }

        if (task.State != TaskState.Claimed)
        {
            throw new InvalidOperationException($"Task '{taskId}' is not claimed.");
        }

        if (!string.Equals(task.ClaimedByNodeId, nodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Task '{taskId}' is not claimed by node '{nodeId}'.");
        }

        return task;
    }

    private static SerializedTaskPayload ClonePayload(SerializedTaskPayload payload)
    {
        return new SerializedTaskPayload(payload.ContentType, payload.Data.ToArray());
    }
}
