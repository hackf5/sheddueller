namespace Sheddueller;

/// <summary>
/// Cluster-wide cleanup configuration for terminal jobs.
/// </summary>
public sealed record JobRetentionCleanupConfiguration(
    bool Enabled,
    TimeSpan? CompletedRetention,
    TimeSpan? FailedRetention,
    TimeSpan? CanceledRetention,
    TimeSpan CleanupInterval,
    int BatchSize)
{
    /// <summary>
    /// Creates cleanup configuration from process-level job retention options.
    /// </summary>
    public static JobRetentionCleanupConfiguration FromOptions(JobRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new JobRetentionCleanupConfiguration(
          options.Enabled,
          options.CompletedRetention,
          options.FailedRetention,
          options.CanceledRetention,
          options.CleanupInterval,
          options.BatchSize);
    }
}
