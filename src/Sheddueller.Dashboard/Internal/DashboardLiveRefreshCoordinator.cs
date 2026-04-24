namespace Sheddueller.Dashboard.Internal;

using System.Diagnostics.CodeAnalysis;

internal sealed class DashboardLiveRefreshCoordinator(
    Func<CancellationToken, Task> refreshAsync,
    Func<Action, Task> applyAsync,
    bool autoRefreshEnabled = true,
    TimeSpan? throttle = null,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null) : IDisposable
{
    private static readonly TimeSpan DefaultThrottle = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync = delayAsync ?? Task.Delay;
    private readonly TimeSpan _throttle = throttle ?? DefaultThrottle;
    private bool _disposed;
    private bool _refreshRequested;
    private bool _refreshLoopRunning;

    public bool AutoRefreshEnabled { get; private set; } = autoRefreshEnabled;

    public bool IsRefreshing { get; private set; }

    public string? RefreshError { get; private set; }

    public DateTimeOffset? LastUpdatedUtc { get; private set; }

    public string StatusClass
      => DashboardFormat.LiveStatusClass(this.IsRefreshing, this.RefreshError);

    public string GetStatusText(DateTimeOffset nowUtc)
      => DashboardFormat.LiveStatusText(this.IsRefreshing, this.RefreshError, this.LastUpdatedUtc, nowUtc);

    public void MarkUpdated(DateTimeOffset? nowUtc = null)
    {
        this.LastUpdatedUtc = nowUtc ?? DateTimeOffset.UtcNow;
        this.RefreshError = null;
        this.IsRefreshing = false;
    }

    public void MarkLiveActivity(DateTimeOffset? nowUtc = null)
    {
        this.LastUpdatedUtc = nowUtc ?? DateTimeOffset.UtcNow;
        this.IsRefreshing = false;
    }

    public void ClearError()
      => this.RefreshError = null;

    public async Task SetAutoRefreshEnabledAsync(bool enabled)
    {
        this.RefreshError = null;
        this.AutoRefreshEnabled = enabled;

        if (enabled)
        {
            await this.RefreshNowAsync().ConfigureAwait(false);
            return;
        }

        lock (this._gate)
        {
            this._refreshRequested = false;
        }
    }

    public void QueueRefresh()
    {
        var startLoop = false;

        lock (this._gate)
        {
            if (this._disposed || !this.AutoRefreshEnabled)
            {
                return;
            }

            this._refreshRequested = true;
            if (!this._refreshLoopRunning)
            {
                this._refreshLoopRunning = true;
                startLoop = true;
            }
        }

        if (startLoop)
        {
            _ = this.RunRefreshLoopAsync(this._disposeCts.Token);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Live dashboard refresh should display provider failures without tearing down the update loop.")]
    public async Task RefreshNowAsync()
    {
        if (this.IsDisposed())
        {
            return;
        }

        var cancellationToken = this._disposeCts.Token;

        await this.ApplyIfActiveAsync(() =>
        {
            this.IsRefreshing = true;
            this.RefreshError = null;
        }).ConfigureAwait(false);

        try
        {
            await refreshAsync(cancellationToken).ConfigureAwait(false);
            await this.ApplyIfActiveAsync(() => this.MarkUpdated()).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await this.ApplyIfActiveAsync(() =>
            {
                this.RefreshError = exception.Message;
                this.IsRefreshing = false;
            }).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (this._gate)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this._refreshRequested = false;
        }

        this._disposeCts.Cancel();
        this._disposeCts.Dispose();
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        var completedCleanly = false;

        try
        {
            while (true)
            {
                await this._delayAsync(this._throttle, cancellationToken).ConfigureAwait(false);

                lock (this._gate)
                {
                    if (this._disposed || !this.AutoRefreshEnabled)
                    {
                        this._refreshLoopRunning = false;
                        completedCleanly = true;
                        return;
                    }

                    if (!this._refreshRequested)
                    {
                        this._refreshLoopRunning = false;
                        completedCleanly = true;
                        return;
                    }

                    this._refreshRequested = false;
                }

                await this.RefreshNowAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completedCleanly = true;
        }
        finally
        {
            lock (this._gate)
            {
                if (!completedCleanly && !this._disposed)
                {
                    this._refreshLoopRunning = false;
                }
            }
        }
    }

    private async Task ApplyIfActiveAsync(Action action)
    {
        if (this.IsDisposed())
        {
            return;
        }

        await applyAsync(() =>
        {
            if (!this.IsDisposed())
            {
                action();
            }
        }).ConfigureAwait(false);
    }

    private bool IsDisposed()
    {
        lock (this._gate)
        {
            return this._disposed;
        }
    }
}
