namespace Sheddueller.Storage;

/// <summary>
/// Store request for recording scheduler worker node liveness.
/// </summary>
public sealed record WorkerNodeHeartbeatRequest(
    string NodeId,
    DateTimeOffset HeartbeatAtUtc,
    int MaxConcurrentExecutionsPerNode,
    int CurrentExecutionCount);
