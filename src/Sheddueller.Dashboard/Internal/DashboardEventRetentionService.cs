namespace Sheddueller.Dashboard.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Sheddueller.Dashboard;

internal sealed class DashboardEventRetentionService(
    IServiceProvider serviceProvider,
    IOptions<ShedduellerDashboardOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await this.CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CleanupOnceAsync(CancellationToken cancellationToken)
    {
        var store = serviceProvider.GetService<IDashboardEventRetentionStore>();
        if (store is null)
        {
            return;
        }

        await store.CleanupAsync(options.Value.EventRetention, cancellationToken).ConfigureAwait(false);
    }
}
