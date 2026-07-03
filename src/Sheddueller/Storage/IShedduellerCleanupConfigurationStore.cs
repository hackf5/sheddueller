namespace Sheddueller.Storage;

/// <summary>
/// Stores cluster-wide cleanup configuration.
/// </summary>
public interface IShedduellerCleanupConfigurationStore
{
    /// <summary>
    /// Gets the full cleanup configuration, seeding any missing settings from the supplied defaults.
    /// </summary>
    ValueTask<ShedduellerCleanupConfiguration> GetCleanupConfigurationAsync(
        ShedduellerCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets terminal job cleanup configuration, seeding it from the supplied default when missing.
    /// </summary>
    ValueTask<JobRetentionCleanupConfiguration> GetJobRetentionCleanupConfigurationAsync(
        JobRetentionCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets durable job-event cleanup configuration, seeding it from the supplied default when missing.
    /// </summary>
    ValueTask<JobEventCleanupConfiguration> GetJobEventCleanupConfigurationAsync(
        JobEventCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics cleanup configuration, seeding it from the supplied default when missing.
    /// </summary>
    ValueTask<MetricsCleanupConfiguration> GetMetricsCleanupConfigurationAsync(
        MetricsCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the full cleanup configuration.
    /// </summary>
    ValueTask SetCleanupConfigurationAsync(
        ShedduellerCleanupConfiguration configuration,
        CancellationToken cancellationToken = default);
}
