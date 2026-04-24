namespace Sheddueller.Dashboard.Components;

using Microsoft.AspNetCore.Components;

using Sheddueller.Dashboard.Internal;

public abstract class DashboardPageComponent : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _timerCancellation = new();
    private IDisposable? _shellRefreshRegistration;
    private PeriodicTimer? _refreshTimer;
    private Task? _refreshTask;
    private bool _disposed;

    [CascadingParameter(Name = "DashboardShellRefresh")]
    public object? ShellRefreshValue { get; set; }

    private protected DashboardLiveRefreshCoordinator LiveRefresh { get; private set; } = null!;

    private DashboardShellRefreshContext? ShellRefresh
      => this.ShellRefreshValue as DashboardShellRefreshContext;

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        await this.DisposePageAsync().ConfigureAwait(false);
        this._shellRefreshRegistration?.Dispose();
        this.LiveRefresh?.Dispose();
        await this._timerCancellation.CancelAsync().ConfigureAwait(false);
        this._refreshTimer?.Dispose();

        if (this._refreshTask is not null)
        {
            await this._refreshTask.ConfigureAwait(false);
        }

        this._timerCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private protected virtual ValueTask DisposePageAsync()
      => ValueTask.CompletedTask;

    private protected void InitializeLiveRefresh(
        Func<CancellationToken, Task> refreshAsync,
        bool showRefreshing,
        Func<DateTimeOffset, DateTimeOffset?> lastUpdatedUtc)
    {
        this.LiveRefresh = new DashboardLiveRefreshCoordinator(
          refreshAsync,
          this.ApplyPageStateAsync);
        this._shellRefreshRegistration = this.ShellRefresh?.Register(
          this.LiveRefresh,
          new DashboardShellRefreshOptions(showRefreshing, lastUpdatedUtc));
    }

    private protected void StartPeriodicRefresh(TimeSpan interval)
    {
        this._refreshTimer = new PeriodicTimer(interval);
        this._refreshTask = this.RunRefreshTimerAsync(this._refreshTimer, this._timerCancellation.Token);
    }

    private protected async Task ApplyPageStateAsync(Action action)
    {
        if (this._disposed)
        {
            return;
        }

        await this.InvokeAsync(() =>
        {
            if (!this._disposed)
            {
                action();
                this.NotifyShellRefreshChanged();
                this.StateHasChanged();
            }
        }).ConfigureAwait(false);
    }

    private protected void NotifyShellRefreshChanged()
      => this.ShellRefresh?.NotifyChanged();

    private async Task RunRefreshTimerAsync(
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (this.LiveRefresh.AutoRefreshEnabled)
                {
                    await this.LiveRefresh.RefreshNowAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
