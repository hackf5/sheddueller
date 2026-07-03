namespace Sheddueller;

/// <summary>
/// Cluster-wide cleanup configuration for durable job events.
/// </summary>
public sealed record JobEventCleanupConfiguration(
    TimeSpan Retention,
    TimeSpan CleanupInterval)
{
    /// <summary>
    /// Gets the default job-event cleanup interval.
    /// </summary>
    public static TimeSpan DefaultCleanupInterval { get; } = TimeSpan.FromHours(1);
}
