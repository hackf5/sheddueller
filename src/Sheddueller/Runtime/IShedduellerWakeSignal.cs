namespace Sheddueller.Runtime;

internal interface IShedduellerWakeSignal
{
    void Notify();

    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
