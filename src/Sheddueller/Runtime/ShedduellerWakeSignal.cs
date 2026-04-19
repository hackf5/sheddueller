#pragma warning disable IDE0130

namespace Sheddueller;

internal sealed class ShedduellerWakeSignal : IShedduellerWakeSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0);
    private int _signaled;

    public void Notify()
    {
        if (Interlocked.Exchange(ref this._signaled, 1) == 0)
        {
            this._signal.Release();
        }
    }

    public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (await this._signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            Volatile.Write(ref this._signaled, 0);
        }
    }

    public void Dispose()
      => this._signal.Dispose();
}
