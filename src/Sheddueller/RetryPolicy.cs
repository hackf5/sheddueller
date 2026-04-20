namespace Sheddueller;

/// <summary>
/// Retry policy for failed job attempts.
/// </summary>
public sealed record RetryPolicy(
    int MaxAttempts,
    RetryBackoffKind BackoffKind,
    TimeSpan BaseDelay,
    TimeSpan? MaxDelay = null);
