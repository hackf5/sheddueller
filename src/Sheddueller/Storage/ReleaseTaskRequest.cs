namespace Sheddueller.Storage;

/// <summary>
/// Store request for releasing a scheduler-interrupted task back to the queue.
/// </summary>
public sealed record ReleaseTaskRequest(
    Guid TaskId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset ReleasedAtUtc);
