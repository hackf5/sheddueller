namespace Sheddueller;

/// <summary>
/// Submission options for an enqueued task.
/// </summary>
public sealed record TaskSubmission(
  int Priority = 0,
  IReadOnlyList<string>? ConcurrencyGroupKeys = null);

/// <summary>
/// Persisted state of a Sheddueller task.
/// </summary>
public enum TaskState
{
    /// <summary>
    /// The task is available for claiming.
    /// </summary>
    Queued,

    /// <summary>
    /// The task is owned by a node and is counted against its concurrency groups.
    /// </summary>
    Claimed,

    /// <summary>
    /// The task completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The task failed.
    /// </summary>
    Failed,
}

/// <summary>
/// Best-effort failure details captured for a failed task.
/// </summary>
public sealed record TaskFailureInfo(
  string ExceptionType,
  string Message,
  string? StackTrace);

/// <summary>
/// Opaque serialized task argument payload.
/// </summary>
public sealed record SerializedTaskPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializedTaskPayload"/> class.
    /// </summary>
    public SerializedTaskPayload(string contentType, byte[] data)
    {
        ContentType = contentType;
        Data = data;
    }

    /// <summary>
    /// Gets the serializer-owned content type.
    /// </summary>
    public string ContentType { get; init; }

#pragma warning disable CA1819
    /// <summary>
    /// Gets the serializer-owned payload bytes.
    /// </summary>
    public byte[] Data { get; init; }
#pragma warning restore CA1819
}

/// <summary>
/// Store request for enqueuing a task.
/// </summary>
public sealed record EnqueueTaskRequest(
  Guid TaskId,
  int Priority,
  string ServiceType,
  string MethodName,
  IReadOnlyList<string> MethodParameterTypes,
  SerializedTaskPayload SerializedArguments,
  IReadOnlyList<string> ConcurrencyGroupKeys,
  DateTimeOffset EnqueuedAtUtc);

/// <summary>
/// Store result for an enqueued task.
/// </summary>
public sealed record EnqueueTaskResult(
  Guid TaskId,
  long EnqueueSequence);

/// <summary>
/// Store request for claiming the next available task.
/// </summary>
public sealed record ClaimTaskRequest(
  string NodeId,
  DateTimeOffset ClaimedAtUtc);

/// <summary>
/// Store result for claim attempts.
/// </summary>
public abstract record ClaimTaskResult
{
    /// <summary>
    /// A task was claimed.
    /// </summary>
    public sealed record Claimed(ClaimedTask Task) : ClaimTaskResult;

    /// <summary>
    /// No task is currently claimable.
    /// </summary>
    public sealed record NoTaskAvailable : ClaimTaskResult;
}

/// <summary>
/// A task claimed by a worker node.
/// </summary>
public sealed record ClaimedTask(
  Guid TaskId,
  long EnqueueSequence,
  int Priority,
  string ServiceType,
  string MethodName,
  IReadOnlyList<string> MethodParameterTypes,
  SerializedTaskPayload SerializedArguments,
  IReadOnlyList<string> ConcurrencyGroupKeys);

/// <summary>
/// Store request for marking a task as completed.
/// </summary>
public sealed record CompleteTaskRequest(
  Guid TaskId,
  string NodeId,
  DateTimeOffset CompletedAtUtc);

/// <summary>
/// Store request for marking a task as failed.
/// </summary>
public sealed record FailTaskRequest(
  Guid TaskId,
  string NodeId,
  DateTimeOffset FailedAtUtc,
  TaskFailureInfo Failure);

/// <summary>
/// Store request for setting a concurrency-group limit.
/// </summary>
public sealed record SetConcurrencyLimitRequest(
  string GroupKey,
  int Limit,
  DateTimeOffset UpdatedAtUtc);
