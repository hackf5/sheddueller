#pragma warning disable IDE0130

namespace Sheddueller;

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
