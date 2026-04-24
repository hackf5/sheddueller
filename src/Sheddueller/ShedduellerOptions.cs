namespace Sheddueller;

/// <summary>
/// Runtime options for Sheddueller.
/// </summary>
public sealed class ShedduellerOptions
{
    /// <summary>
    /// Gets or sets the node identifier. When omitted, a process-instance identifier is generated.
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of jobs this node executes concurrently.
    /// </summary>
    public int MaxConcurrentExecutionsPerNode { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets the fallback polling interval used when no wake signal is received.
    /// </summary>
    public TimeSpan IdlePollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the lease duration for claimed jobs.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the interval used by workers to renew claimed job leases.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long after its last heartbeat a worker node is considered stale. Null uses LeaseDuration plus HeartbeatInterval.
    /// </summary>
    public TimeSpan? StaleNodeThreshold { get; set; }

    /// <summary>
    /// Gets or sets how long after its last heartbeat a worker node is considered dead. Null uses four times LeaseDuration.
    /// </summary>
    public TimeSpan? DeadNodeThreshold { get; set; }

    /// <summary>
    /// Gets or sets the default retry policy applied when submissions and schedules do not specify one.
    /// </summary>
    public RetryPolicy? DefaultRetryPolicy { get; set; }

    /// <summary>
    /// Gets the effective stale worker node threshold.
    /// </summary>
    public TimeSpan EffectiveStaleNodeThreshold
      => this.StaleNodeThreshold ?? this.LeaseDuration.Add(this.HeartbeatInterval);

    /// <summary>
    /// Gets the effective dead worker node threshold.
    /// </summary>
    public TimeSpan EffectiveDeadNodeThreshold
      => this.DeadNodeThreshold ?? TimeSpan.FromTicks(this.LeaseDuration.Ticks * 4);
}
