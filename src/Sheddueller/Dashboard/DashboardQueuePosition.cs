namespace Sheddueller.Dashboard;

/// <summary>
/// Dynamic dashboard queue position.
/// </summary>
public sealed record DashboardQueuePosition(
    Guid JobId,
    DashboardQueuePositionKind Kind,
    long? Position,
    string? Reason = null);
