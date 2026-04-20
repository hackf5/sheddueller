namespace Sheddueller;

/// <summary>
/// Controls whether recurring occurrences may overlap.
/// </summary>
public enum RecurringOverlapMode
{
    /// <summary>
    /// Drops a due occurrence when an earlier occurrence is still queued or claimed.
    /// </summary>
    Skip,

    /// <summary>
    /// Allows each due occurrence to create a job regardless of earlier non-terminal occurrences.
    /// </summary>
    Allow,
}
