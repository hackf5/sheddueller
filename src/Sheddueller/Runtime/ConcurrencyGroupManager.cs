namespace Sheddueller.Runtime;

using Microsoft.Extensions.Logging;

using Sheddueller.Enqueueing;
using Sheddueller.Storage;

internal sealed class ConcurrencyGroupManager(
    IJobStore store,
    TimeProvider timeProvider,
    IShedduellerWakeSignal wakeSignal,
    ILogger<ConcurrencyGroupManager> logger) : IConcurrencyGroupManager
{
    public async ValueTask SetLimitAsync(string groupKey, int limit, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);
        ValidateLimit(limit);

        await store
          .SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest(groupKey, limit, timeProvider.GetUtcNow()), cancellationToken)
          .ConfigureAwait(false);
        wakeSignal.Notify();
        logger.ConcurrencyGroupLimitSet(groupKey, limit);
    }

    public async ValueTask SetDefaultLimitAsync(string groupKey, int limit, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);
        ValidateLimit(limit);

        await store
          .SetConcurrencyDefaultLimitAsync(new SetConcurrencyDefaultLimitRequest(groupKey, limit, timeProvider.GetUtcNow()), cancellationToken)
          .ConfigureAwait(false);
        wakeSignal.Notify();
        logger.ConcurrencyGroupDefaultLimitSet(groupKey, limit);
    }

    public async ValueTask ClearLimitOverrideAsync(string groupKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);

        await store
          .ClearConcurrencyLimitOverrideAsync(new ClearConcurrencyLimitOverrideRequest(groupKey, timeProvider.GetUtcNow()), cancellationToken)
          .ConfigureAwait(false);
        wakeSignal.Notify();
        logger.ConcurrencyGroupLimitOverrideCleared(groupKey);
    }

    public ValueTask<int?> GetConfiguredLimitAsync(string groupKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);

        return store.GetConfiguredConcurrencyLimitAsync(groupKey, cancellationToken);
    }

    private static void ValidateLimit(int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Concurrency group limits must be positive.");
        }
    }
}
