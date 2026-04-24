namespace Sheddueller.Dashboard.Internal;

internal sealed class DashboardShellRefreshContext
{
    private readonly Lock _gate = new();
    private DashboardShellRefreshRegistration? _current;

    public event Action? Changed;

    public DashboardShellRefreshRegistration? Current
    {
        get
        {
            lock (this._gate)
            {
                return this._current;
            }
        }
    }

    public IDisposable Register(
        DashboardLiveRefreshCoordinator coordinator,
        DashboardShellRefreshOptions options)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(options);

        var registration = new DashboardShellRefreshRegistration(this, coordinator, options);
        lock (this._gate)
        {
            this._current = registration;
        }

        this.NotifyChanged();
        return registration;
    }

    public async Task ToggleCurrentAutoRefreshAsync()
    {
        var current = this.Current;
        if (current is null)
        {
            return;
        }

        await current.Coordinator.SetAutoRefreshEnabledAsync(!current.Coordinator.AutoRefreshEnabled).ConfigureAwait(false);
        this.NotifyChanged();
    }

    public void NotifyChanged()
      => this.Changed?.Invoke();

    internal void Unregister(DashboardShellRefreshRegistration registration)
    {
        lock (this._gate)
        {
            if (!ReferenceEquals(this._current, registration))
            {
                return;
            }

            this._current = null;
        }

        this.NotifyChanged();
    }
}

internal sealed record DashboardShellRefreshOptions(
    bool ShowRefreshing,
    Func<DateTimeOffset, DateTimeOffset?> LastUpdatedUtc);

internal sealed class DashboardShellRefreshRegistration(
    DashboardShellRefreshContext owner,
    DashboardLiveRefreshCoordinator coordinator,
    DashboardShellRefreshOptions options) : IDisposable
{
    public DashboardLiveRefreshCoordinator Coordinator { get; } = coordinator;

    public bool ShowRefreshing { get; } = options.ShowRefreshing;

    public DateTimeOffset? GetLastUpdatedUtc(DateTimeOffset nowUtc)
      => options.LastUpdatedUtc(nowUtc);

    public void Dispose()
      => owner.Unregister(this);
}
