namespace Sheddueller.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
  private DateTimeOffset utcNow;

  public ManualTimeProvider(DateTimeOffset utcNow)
  {
    this.utcNow = utcNow;
  }

  public override DateTimeOffset GetUtcNow()
  {
    return utcNow;
  }

  public void SetUtcNow(DateTimeOffset value)
  {
    utcNow = value;
  }
}
