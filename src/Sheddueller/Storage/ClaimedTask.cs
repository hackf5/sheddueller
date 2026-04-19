#pragma warning disable IDE0130

namespace Sheddueller;

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
