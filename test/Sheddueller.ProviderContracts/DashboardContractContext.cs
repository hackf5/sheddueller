namespace Sheddueller.ProviderContracts;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

public sealed class DashboardContractContext(
    ITaskStore store,
    IDashboardJobReader reader,
    IDashboardEventSink eventSink,
    IDashboardEventRetentionStore retentionStore,
    IAsyncDisposable? asyncDisposable = null) : IAsyncDisposable
{
    public ITaskStore Store { get; } = store;

    public IDashboardJobReader Reader { get; } = reader;

    public IDashboardEventSink EventSink { get; } = eventSink;

    public IDashboardEventRetentionStore RetentionStore { get; } = retentionStore;

    public async ValueTask DisposeAsync()
    {
        if (asyncDisposable is not null)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
