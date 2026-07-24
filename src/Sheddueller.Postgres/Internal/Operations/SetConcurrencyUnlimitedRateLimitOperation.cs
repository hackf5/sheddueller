namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class SetConcurrencyUnlimitedRateLimitOperation
{
    public static ValueTask ExecuteAsync(
        PostgresOperationContext context,
        SetConcurrencyUnlimitedRateLimitRequest request,
        CancellationToken cancellationToken)
      => PostgresConcurrencyRateLimits.UpdateAsync(
        context,
        request.GroupKey,
        current => current with
        {
            OverrideEnabled = true,
            ConfiguredRateLimit = null,
        },
        cancellationToken);
}
