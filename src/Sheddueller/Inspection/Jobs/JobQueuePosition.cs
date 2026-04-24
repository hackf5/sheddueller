namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Dynamic job queue position.
/// </summary>
public sealed record JobQueuePosition(
    Guid JobId,
    JobQueuePositionKind Kind,
    long? Position,
    string? Reason = null);
