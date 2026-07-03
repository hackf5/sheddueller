namespace Sheddueller.Storage;

/// <summary>
/// Store request for deleting terminal jobs older than configured cutoffs.
/// </summary>
public sealed record JobRetentionCleanupRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JobRetentionCleanupRequest"/> class.
    /// </summary>
    public JobRetentionCleanupRequest(
        DateTimeOffset? completedBeforeUtc,
        DateTimeOffset? failedBeforeUtc,
        DateTimeOffset? canceledBeforeUtc,
        int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Retention cleanup batch size must be positive.");
        }

        this.CompletedBeforeUtc = completedBeforeUtc;
        this.FailedBeforeUtc = failedBeforeUtc;
        this.CanceledBeforeUtc = canceledBeforeUtc;
        this.BatchSize = batchSize;
    }

    /// <summary>
    /// Gets the exclusive cutoff for completed jobs. Null keeps completed jobs.
    /// </summary>
    public DateTimeOffset? CompletedBeforeUtc { get; }

    /// <summary>
    /// Gets the exclusive cutoff for failed jobs. Null keeps failed jobs.
    /// </summary>
    public DateTimeOffset? FailedBeforeUtc { get; }

    /// <summary>
    /// Gets the exclusive cutoff for canceled jobs. Null keeps canceled jobs.
    /// </summary>
    public DateTimeOffset? CanceledBeforeUtc { get; }

    /// <summary>
    /// Gets the maximum number of jobs to delete.
    /// </summary>
    public int BatchSize { get; }
}
