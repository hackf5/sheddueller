namespace Sheddueller.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        utcNow = value;
    }
}
