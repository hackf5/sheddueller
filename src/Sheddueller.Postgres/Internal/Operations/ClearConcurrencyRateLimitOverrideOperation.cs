namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class ClearConcurrencyRateLimitOverrideOperation
{
    public static ValueTask ExecuteAsync(
        PostgresOperationContext context,
        ClearConcurrencyRateLimitOverrideRequest request,
        CancellationToken cancellationToken)
      => PostgresConcurrencyRateLimits.UpdateAsync(
        context,
        request.GroupKey,
        current => current with
        {
            OverrideEnabled = false,
            ConfiguredRateLimit = null,
        },
        cancellationToken);
}
