namespace Sheddueller;

/// <summary>
/// Retry policy for failed job attempts.
/// </summary>
/// <param name="MaxAttempts">The maximum number of attempts, including the first execution.</param>
/// <param name="BackoffKind">The retry delay strategy.</param>
/// <param name="BaseDelay">The first retry delay for failed jobs.</param>
/// <param name="MaxDelay">The optional upper bound for computed retry delays.</param>
public sealed record RetryPolicy(
    int MaxAttempts,
    RetryBackoffKind BackoffKind,
    TimeSpan BaseDelay,
    TimeSpan? MaxDelay = null);
