namespace Sheddueller.Testing;

/// <summary>
/// Captured jobs for a dependency-injected <see cref="CapturingJobEnqueuer"/>.
/// </summary>
public sealed class JobEnqueuerCapture : IDisposable
{
    private readonly CapturingJobEnqueuer _owner;
    private bool _disposed;

    internal JobEnqueuerCapture(
      FakeJobEnqueuer fake,
      CapturingJobEnqueuer owner)
    {
        this.Fake = fake;
        this._owner = owner;
    }

    /// <summary>
    /// Gets the fake enqueuer containing jobs captured in this scope.
    /// </summary>
    public FakeJobEnqueuer Fake { get; }

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
