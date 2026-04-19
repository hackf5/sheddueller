#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Store request for marking a task as completed.
/// </summary>
public sealed record CompleteTaskRequest(
    Guid TaskId,
    string NodeId,
    DateTimeOffset CompletedAtUtc);
