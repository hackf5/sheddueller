namespace Sheddueller.Storage;

/// <summary>
/// Store request for canceling a queued job.
/// </summary>
public sealed record CancelJobRequest(
    Guid JobId,
    DateTimeOffset CanceledAtUtc);
