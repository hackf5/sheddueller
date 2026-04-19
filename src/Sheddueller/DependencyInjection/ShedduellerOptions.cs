#pragma warning disable IDE0130

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
    /// Gets or sets the maximum number of tasks this node executes concurrently.
    /// </summary>
    public int MaxConcurrentExecutionsPerNode { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets the fallback polling interval used when no wake signal is received.
    /// </summary>
    public TimeSpan IdlePollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}
