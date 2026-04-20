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

    public static TimeSpan CalculateBackoff(PostgresClaimedJob job)
    {
        if (job.RetryBackoffKind is null || job.RetryBaseDelay is null)
        {
            return TimeSpan.Zero;
        }

        if (job.RetryBackoffKind == RetryBackoffKind.Fixed)
        {
            return job.RetryBaseDelay.Value;
        }

        var multiplier = Math.Pow(2, job.AttemptCount - 1);
        var ticks = job.RetryBaseDelay.Value.Ticks * multiplier;
        var delay = TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, ticks));

        return job.RetryMaxDelay is { } maxDelay && delay > maxDelay ? maxDelay : delay;
    }
}
