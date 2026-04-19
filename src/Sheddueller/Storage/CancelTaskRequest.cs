namespace Sheddueller.Storage;

/// <summary>
/// Store request for canceling a queued task.
/// </summary>
public sealed record CancelTaskRequest(
    Guid TaskId,
    DateTimeOffset CanceledAtUtc);
