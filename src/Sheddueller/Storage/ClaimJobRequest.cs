namespace Sheddueller.Storage;

/// <summary>
/// Store request for claiming the next available job.
/// </summary>
public sealed record ClaimJobRequest(
    string NodeId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset LeaseExpiresAtUtc);
