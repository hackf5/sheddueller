namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class ClearConcurrencyDefaultRateLimitOperation
{
    public static ValueTask ExecuteAsync(
        PostgresOperationContext context,
        ClearConcurrencyDefaultRateLimitRequest request,
        CancellationToken cancellationToken)
      => PostgresConcurrencyRateLimits.UpdateAsync(
        context,
        request.GroupKey,
        current => current with { DefaultRateLimit = null },
        cancellationToken);
}
