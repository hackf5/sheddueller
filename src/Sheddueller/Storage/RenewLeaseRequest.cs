namespace Sheddueller.Storage;

/// <summary>
/// Store request for renewing a claimed job lease.
/// </summary>
public sealed record RenewLeaseRequest(
    Guid JobId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset HeartbeatAtUtc,
    DateTimeOffset LeaseExpiresAtUtc);
