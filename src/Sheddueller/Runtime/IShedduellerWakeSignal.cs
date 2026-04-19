#pragma warning disable IDE0130

namespace Sheddueller;

internal interface IShedduellerWakeSignal
{
    void Notify();

    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
