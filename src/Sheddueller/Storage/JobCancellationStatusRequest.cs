namespace Sheddueller.Storage;

/// <summary>
/// Store request for reading a running job's cooperative cancellation request.
/// </summary>
public sealed record JobCancellationStatusRequest(
    Guid JobId,
    string NodeId,
    Guid LeaseToken,
    DateTimeOffset ObservedAtUtc);
