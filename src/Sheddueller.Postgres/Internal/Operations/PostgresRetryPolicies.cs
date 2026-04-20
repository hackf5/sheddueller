namespace Sheddueller.Postgres.Internal.Operations;

internal static class PostgresRetryPolicies
{
    public static PostgresRetryPolicy Normalize(RetryPolicy? retryPolicy)
    {
        if (retryPolicy is not { MaxAttempts: > 1 })
        {
            return new PostgresRetryPolicy(false, 1, null, null, null);
        }

        return new PostgresRetryPolicy(true, retryPolicy.MaxAttempts, retryPolicy.BackoffKind, retryPolicy.BaseDelay, retryPolicy.MaxDelay);
    }

    public static TimeSpan CalculateBackoff(PostgresClaimedTask task)
    {
        if (task.RetryBackoffKind is null || task.RetryBaseDelay is null)
        {
            return TimeSpan.Zero;
        }

        if (task.RetryBackoffKind == RetryBackoffKind.Fixed)
        {
            return task.RetryBaseDelay.Value;
        }

        var multiplier = Math.Pow(2, task.AttemptCount - 1);
        var ticks = task.RetryBaseDelay.Value.Ticks * multiplier;
        var delay = TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, ticks));

        return task.RetryMaxDelay is { } maxDelay && delay > maxDelay ? maxDelay : delay;
    }
}
