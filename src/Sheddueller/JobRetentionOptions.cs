namespace Sheddueller;

/// <summary>
/// Configures cleanup of terminal jobs from the operational store.
/// </summary>
public sealed class JobRetentionOptions
{
    /// <summary>
    /// Gets or sets whether terminal job retention cleanup is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how long completed jobs remain in the operational store. Null retains them forever.
    /// </summary>
    public TimeSpan? CompletedRetention { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets how long failed jobs remain in the operational store. Null retains them forever.
    /// </summary>
    public TimeSpan? FailedRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how long canceled jobs remain in the operational store. Null retains them forever.
    /// </summary>
    public TimeSpan? CanceledRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how often background retention cleanup runs.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of jobs deleted in one cleanup transaction.
    /// </summary>
    public int BatchSize { get; set; } = 1000;
}
