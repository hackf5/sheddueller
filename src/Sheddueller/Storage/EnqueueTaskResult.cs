namespace Sheddueller.Storage;

/// <summary>
/// Store result for an enqueued task.
/// </summary>
public sealed record EnqueueTaskResult(
    Guid TaskId,
    long EnqueueSequence);
