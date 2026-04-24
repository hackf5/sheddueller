namespace Sheddueller.Runtime;

internal interface IShedduellerJobEventListener
{
    Task ListenAsync(CancellationToken cancellationToken);
}
