namespace Sheddueller.Testing;

/// <summary>
/// Captured recurring schedules for a dependency-injected <see cref="CapturingRecurringScheduleManager"/>.
/// </summary>
public sealed class RecurringScheduleManagerCapture : IDisposable
{
    private readonly CapturingRecurringScheduleManager _owner;
    private bool _disposed;

    internal RecurringScheduleManagerCapture(
      FakeRecurringScheduleManager fake,
      CapturingRecurringScheduleManager owner)
    {
        this.Fake = fake;
        this._owner = owner;
    }

    /// <summary>
    /// Gets the fake manager containing recurring schedules captured in this scope.
    /// </summary>
    public FakeRecurringScheduleManager Fake { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._owner.ClearCapture(this.Fake);
        this._disposed = true;
    }
}
