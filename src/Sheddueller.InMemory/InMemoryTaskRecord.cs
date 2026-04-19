namespace Sheddueller;

internal sealed class InMemoryTaskRecord(
  Guid taskId,
  TaskState state,
  int priority,
  long enqueueSequence,
  DateTimeOffset enqueuedAtUtc,
  string serviceType,
  string methodName,
  IReadOnlyList<string> methodParameterTypes,
  SerializedTaskPayload serializedArguments,
  IReadOnlyList<string> concurrencyGroupKeys)
{
    public Guid TaskId { get; } = taskId;

    public TaskState State { get; set; } = state;

    public int Priority { get; } = priority;

    public long EnqueueSequence { get; } = enqueueSequence;

    public DateTimeOffset EnqueuedAtUtc { get; } = enqueuedAtUtc;

    public string ServiceType { get; } = serviceType;

    public string MethodName { get; } = methodName;

    public IReadOnlyList<string> MethodParameterTypes { get; } = methodParameterTypes;

    public SerializedTaskPayload SerializedArguments { get; } = serializedArguments;

    public IReadOnlyList<string> ConcurrencyGroupKeys { get; } = concurrencyGroupKeys;

    public string? ClaimedByNodeId { get; set; }

    public DateTimeOffset? ClaimedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? FailedAtUtc { get; set; }

    public TaskFailureInfo? Failure { get; set; }
}

internal sealed record InMemoryTaskSnapshot(
  Guid TaskId,
  TaskState State,
  int Priority,
  long EnqueueSequence,
  DateTimeOffset EnqueuedAtUtc,
  string ServiceType,
  string MethodName,
  IReadOnlyList<string> MethodParameterTypes,
  SerializedTaskPayload SerializedArguments,
  IReadOnlyList<string> ConcurrencyGroupKeys,
  string? ClaimedByNodeId,
  DateTimeOffset? ClaimedAtUtc,
  DateTimeOffset? CompletedAtUtc,
  DateTimeOffset? FailedAtUtc,
  TaskFailureInfo? Failure);
