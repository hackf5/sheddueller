namespace Sheddueller.Storage;

/// <summary>
/// Store request for recovering claims whose leases have expired.
/// </summary>
public sealed record RecoverExpiredLeasesRequest(
    DateTimeOffset RecoveredAtUtc);
