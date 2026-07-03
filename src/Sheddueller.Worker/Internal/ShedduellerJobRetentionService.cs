namespace Sheddueller.Worker.Internal;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheddueller.Storage;

internal sealed class ShedduellerJobRetentionService(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    IOptions<ShedduellerOptions> options,
    ILogger<ShedduellerJobRetentionService> logger) : BackgroundService
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Retention cleanup failures are diagnostic and should not stop the worker host.")]
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
                    logger.WorkerJobRetentionCleanupFailed(exception);
                }

                await Task.Delay(options.Value.JobRetention.CleanupInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask CleanupOnceAsync(CancellationToken cancellationToken)
    {
        var retention = options.Value.JobRetention;
        if (!retention.Enabled)
        {
            return;
        }

        var store = serviceProvider.GetService<IJobRetentionStore>();
        if (store is null)
        {
            logger.WorkerJobRetentionStoreMissing();
            return;
        }

        if (retention.CompletedRetention is null
            && retention.FailedRetention is null
            && retention.CanceledRetention is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var request = new JobRetentionCleanupRequest(
          retention.CompletedRetention is { } completedRetention ? now.Subtract(completedRetention) : null,
          retention.FailedRetention is { } failedRetention ? now.Subtract(failedRetention) : null,
          retention.CanceledRetention is { } canceledRetention ? now.Subtract(canceledRetention) : null,
          retention.BatchSize);

        var totalDeleted = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await store.CleanupTerminalJobsAsync(request, cancellationToken).ConfigureAwait(false);
            totalDeleted += result.DeletedCount;
            if (result.DeletedCount < retention.BatchSize)
            {
                break;
            }
        }

        if (totalDeleted > 0)
        {
            logger.WorkerJobRetentionCleaned(totalDeleted);
        }
    }
}
