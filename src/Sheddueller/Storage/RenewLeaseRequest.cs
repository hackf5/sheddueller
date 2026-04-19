namespace Sheddueller.Storage;

/// <summary>
/// Store request for renewing a claimed task lease.
/// </summary>
public sealed record RenewLeaseRequest(
    Guid TaskId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset HeartbeatAtUtc,
    DateTimeOffset LeaseExpiresAtUtc);
