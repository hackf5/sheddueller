namespace Sheddueller.Storage;

/// <summary>
/// Store request for claiming the next available task.
/// </summary>
public sealed record ClaimTaskRequest(
    string NodeId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset LeaseExpiresAtUtc);
