namespace Sheddueller.Runtime;

using Sheddueller.Enqueueing;
using Sheddueller.Storage;

internal sealed class ConcurrencyGroupManager(IJobStore store, TimeProvider timeProvider, IShedduellerWakeSignal wakeSignal) : IConcurrencyGroupManager
{
    public async ValueTask SetLimitAsync(string groupKey, int limit, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Concurrency group limits must be positive.");
        }

        await store
          .SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest(groupKey, limit, timeProvider.GetUtcNow()), cancellationToken)
          .ConfigureAwait(false);
        wakeSignal.Notify();
    }

    public ValueTask<int?> GetConfiguredLimitAsync(string groupKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateConcurrencyGroupKey(groupKey);

        return store.GetConfiguredConcurrencyLimitAsync(groupKey, cancellationToken);
    }
}
