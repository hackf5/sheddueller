namespace Sheddueller.Storage;

/// <summary>
/// Store request for canceling all queued jobs.
/// </summary>
public sealed record CancelQueuedJobsRequest(
    DateTimeOffset CanceledAtUtc);
