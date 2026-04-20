namespace Sheddueller.ProviderContracts;

using Sheddueller.Storage;

public sealed class JobStoreContractContext(
    IJobStore store,
    Func<string, ValueTask>? makeScheduleDueAsync = null,
    IAsyncDisposable? asyncDisposable = null) : IAsyncDisposable
{
    public IJobStore Store { get; } = store;

    public ValueTask MakeScheduleDueAsync(string scheduleKey)
      => makeScheduleDueAsync?.Invoke(scheduleKey) ?? ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (asyncDisposable is not null)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
