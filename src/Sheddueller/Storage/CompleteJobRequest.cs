namespace Sheddueller.Storage;

/// <summary>
/// Store request for marking a job as completed.
/// </summary>
public sealed record CompleteJobRequest(
    Guid JobId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset CompletedAtUtc);
