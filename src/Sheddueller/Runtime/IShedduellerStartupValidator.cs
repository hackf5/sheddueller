namespace Sheddueller.Runtime;

internal interface IShedduellerStartupValidator
{
    ValueTask ValidateAsync(CancellationToken cancellationToken);
}
