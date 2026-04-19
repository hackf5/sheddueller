namespace Sheddueller;

internal sealed class InMemoryTaskRecord
{
  public InMemoryTaskRecord(
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
    TaskId = taskId;
    State = state;
    Priority = priority;
    EnqueueSequence = enqueueSequence;
    EnqueuedAtUtc = enqueuedAtUtc;
    ServiceType = serviceType;
    MethodName = methodName;
    MethodParameterTypes = methodParameterTypes;
    SerializedArguments = serializedArguments;
    ConcurrencyGroupKeys = concurrencyGroupKeys;
  }

  public Guid TaskId { get; }

  public TaskState State { get; set; }

  public int Priority { get; }

  public long EnqueueSequence { get; }

  public DateTimeOffset EnqueuedAtUtc { get; }

  public string ServiceType { get; }

  public string MethodName { get; }

  public IReadOnlyList<string> MethodParameterTypes { get; }

  public SerializedTaskPayload SerializedArguments { get; }

  public IReadOnlyList<string> ConcurrencyGroupKeys { get; }

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
