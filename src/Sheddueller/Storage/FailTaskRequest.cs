namespace Sheddueller.Storage;

/// <summary>
/// Store request for marking a task as failed.
/// </summary>
public sealed record FailTaskRequest(
    Guid TaskId,
    string NodeId,
    DateTimeOffset FailedAtUtc,
    TaskFailureInfo Failure);
