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
    TimeProvider timeProvider,
    IOptions<ShedduellerDashboardOptions> options,
    ILogger<JobEventRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan PersistedSettingsPollingInterval = TimeSpan.FromMinutes(1);
    private DateTimeOffset? _lastCleanupAtUtc;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Retention cleanup failures are diagnostic and should not stop the dashboard host.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = PersistedSettingsPollingInterval;
                try
                {
                    delay = await this.CleanupIfDueAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.DashboardEventRetentionCleanupFailed(exception);
                }

                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<TimeSpan> CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var settingsStore = serviceProvider.GetService<IShedduellerCleanupConfigurationStore>();
        var configuration = settingsStore is null
          ? this.CreateDefaultConfiguration()
          : await settingsStore
            .GetJobEventCleanupConfigurationAsync(this.CreateDefaultConfiguration(), cancellationToken)
            .ConfigureAwait(false);
        var usesPersistedSettings = settingsStore is not null;
        var now = timeProvider.GetUtcNow();
        if (usesPersistedSettings
            && this._lastCleanupAtUtc is { } lastCleanupAtUtc
            && now - lastCleanupAtUtc < configuration.CleanupInterval)
        {
            return GetDelay(configuration.CleanupInterval - (now - lastCleanupAtUtc), usesPersistedSettings);
        }

        var store = serviceProvider.GetService<IJobEventRetentionStore>();
        if (store is null)
        {
            logger.DashboardEventRetentionStoreMissing();
            return GetDelay(configuration.CleanupInterval, usesPersistedSettings);
        }

        var deleted = await store.CleanupAsync(configuration.Retention, cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            logger.DashboardEventRetentionCleaned(deleted);
        }

        this._lastCleanupAtUtc = now;

        return GetDelay(configuration.CleanupInterval, usesPersistedSettings);
    }

    private JobEventCleanupConfiguration CreateDefaultConfiguration()
      => new(options.Value.EventRetention, JobEventCleanupConfiguration.DefaultCleanupInterval);

    private static TimeSpan GetDelay(
        TimeSpan requestedDelay,
        bool usesPersistedSettings)
      => usesPersistedSettings && requestedDelay > PersistedSettingsPollingInterval
        ? PersistedSettingsPollingInterval
        : requestedDelay;
}
