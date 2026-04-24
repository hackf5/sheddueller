namespace Sheddueller.Inspection.Nodes;

/// <summary>
/// Worker node inspection list item.
/// </summary>
public sealed record NodeInspectionSummary(
    string NodeId,
    NodeHealthState State,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    int ClaimedJobCount,
    int MaxConcurrentExecutionsPerNode,
    int CurrentExecutionCount);
