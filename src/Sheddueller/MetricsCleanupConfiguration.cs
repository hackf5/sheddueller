namespace Sheddueller;

/// <summary>
/// Cluster-wide cleanup configuration for persisted metrics rollups.
/// </summary>
public sealed record MetricsCleanupConfiguration(
    TimeSpan Retention,
    TimeSpan CleanupInterval)
{
    /// <summary>
    /// Gets the default metrics cleanup configuration.
    /// </summary>
    public static MetricsCleanupConfiguration Default { get; } = new(
      TimeSpan.FromDays(7),
      TimeSpan.FromHours(1));
}
