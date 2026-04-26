namespace Sheddueller.Dashboard.Internal;

using Microsoft.Extensions.Hosting;

internal sealed class DashboardThroughputHostedService(DashboardThroughputStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = store;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
      => Task.CompletedTask;
}
