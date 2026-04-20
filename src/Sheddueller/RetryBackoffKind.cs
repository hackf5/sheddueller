namespace Sheddueller;

/// <summary>
/// Retry backoff strategy for failed job attempts.
/// </summary>
public enum RetryBackoffKind
{
    /// <summary>
    /// Uses the same delay after each failed attempt.
    /// </summary>
    Fixed,

    /// <summary>
    /// Doubles the delay after each failed attempt until the configured maximum is reached.
    /// </summary>
    Exponential,
}
