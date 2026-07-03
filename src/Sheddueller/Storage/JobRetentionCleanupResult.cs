namespace Sheddueller.Storage;

/// <summary>
/// Result of a terminal job retention cleanup batch.
/// </summary>
public sealed record JobRetentionCleanupResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JobRetentionCleanupResult"/> class.
    /// </summary>
    public JobRetentionCleanupResult(int deletedCount)
    {
        if (deletedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deletedCount), deletedCount, "Deleted count cannot be negative.");
        }

        this.DeletedCount = deletedCount;
    }

    /// <summary>
    /// Gets the number of terminal jobs deleted.
    /// </summary>
    public int DeletedCount { get; }
}
