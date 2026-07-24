namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class SetConcurrencyRateLimitOperation
{
    public static ValueTask ExecuteAsync(
        PostgresOperationContext context,
        SetConcurrencyRateLimitRequest request,
        CancellationToken cancellationToken)
      => PostgresConcurrencyRateLimits.UpdateAsync(
        context,
        request.GroupKey,
        current => current with
        {
            OverrideEnabled = true,
            ConfiguredRateLimit = request.RateLimit,
        },
        cancellationToken);
}
