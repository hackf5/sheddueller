namespace Sheddueller.Dashboard.Internal;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sheddueller.Runtime;

internal sealed class DashboardJobEventListenerService(
    IEnumerable<IShedduellerJobEventListener> listeners,
    ILogger<DashboardJobEventListenerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var snapshot = listeners.ToArray();
        logger.DashboardJobEventListenerServiceStarted(snapshot.Length);
        return snapshot.Length == 0
          ? Task.CompletedTask
          : Task.WhenAll(snapshot.Select(listener => listener.ListenAsync(stoppingToken)));
    }
}
