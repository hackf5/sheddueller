namespace Sheddueller.Dashboard.Internal;

using Microsoft.Extensions.Hosting;

using Sheddueller.Runtime;

internal sealed class DashboardJobEventListenerService(
    IEnumerable<IShedduellerJobEventListener> listeners) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var snapshot = listeners.ToArray();
        return snapshot.Length == 0
          ? Task.CompletedTask
          : Task.WhenAll(snapshot.Select(listener => listener.ListenAsync(stoppingToken)));
    }
}
