namespace Sheddueller.Storage;

/// <summary>
/// Store request for canceling a queued job or requesting cooperative cancellation for a running job.
/// </summary>
public sealed record CancelJobRequest(
    Guid JobId,
    DateTimeOffset CanceledAtUtc);
