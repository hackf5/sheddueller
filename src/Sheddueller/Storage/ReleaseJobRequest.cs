namespace Sheddueller.Storage;

/// <summary>
/// Store request for releasing a scheduler-interrupted job back to the queue.
/// </summary>
public sealed record ReleaseJobRequest(
    Guid JobId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset ReleasedAtUtc);
