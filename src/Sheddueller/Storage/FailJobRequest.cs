namespace Sheddueller.Storage;

/// <summary>
/// Store request for marking a job as failed.
/// </summary>
public sealed record FailJobRequest(
    Guid JobId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset FailedAtUtc,
    JobFailureInfo Failure);
