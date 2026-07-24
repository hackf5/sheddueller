namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class SetConcurrencyDefaultRateLimitOperation
{
    public static ValueTask ExecuteAsync(
        PostgresOperationContext context,
        SetConcurrencyDefaultRateLimitRequest request,
        CancellationToken cancellationToken)
      => PostgresConcurrencyRateLimits.UpdateAsync(
        context,
        request.GroupKey,
        current => current with { DefaultRateLimit = request.RateLimit },
        cancellationToken);
}
