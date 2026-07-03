namespace Sheddueller.ProviderContracts;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Storage;

public sealed class JobRetentionStoreContractContext(
    IJobStore store,
    IJobRetentionStore retentionStore,
    IJobInspectionReader reader,
    IAsyncDisposable? asyncDisposable = null) : IAsyncDisposable
{
    public IJobStore Store { get; } = store;

    public IJobRetentionStore RetentionStore { get; } = retentionStore;

    public IJobInspectionReader Reader { get; } = reader;

    public async ValueTask DisposeAsync()
    {
        if (asyncDisposable is not null)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
