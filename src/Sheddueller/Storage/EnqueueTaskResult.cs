#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Store result for an enqueued task.
/// </summary>
public sealed record EnqueueTaskResult(
    Guid TaskId,
    long EnqueueSequence);
