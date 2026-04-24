namespace Sheddueller.Storage;

/// <summary>
/// Store request for marking cooperative job cancellation as observed.
/// </summary>
public sealed record ObserveJobCancellationRequest(
    Guid JobId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset ObservedAtUtc);
