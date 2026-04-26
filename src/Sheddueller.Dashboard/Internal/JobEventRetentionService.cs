namespace Sheddueller.Dashboard.Internal;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal sealed class JobEventRetentionService(
    IServiceProvider serviceProvider,
    IOptions<ShedduellerDashboardOptions> options,
    ILogger<JobEventRetentionService> logger) : BackgroundService
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Retention cleanup failures are diagnostic and should not stop the dashboard host.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await this.CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.DashboardEventRetentionCleanupFailed(exception);
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask CleanupOnceAsync(CancellationToken cancellationToken)
    {
        var store = serviceProvider.GetService<IJobEventRetentionStore>();
        if (store is null)
        {
            logger.DashboardEventRetentionStoreMissing();
            return;
        }

        var deleted = await store.CleanupAsync(options.Value.EventRetention, cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            logger.DashboardEventRetentionCleaned(deleted);
        }
    }
}
