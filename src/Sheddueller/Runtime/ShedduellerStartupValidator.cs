#pragma warning disable IDE0130

namespace Sheddueller;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal sealed class ShedduellerStartupValidator(
    IServiceProvider serviceProvider,
    IOptions<ShedduellerOptions> options) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IOptions<ShedduellerOptions> _options = options;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var value = this._options.Value;

        if (value.NodeId is not null && value.NodeId.Length == 0)
        {
            throw new InvalidOperationException("ShedduellerOptions.NodeId must be null or a non-empty string.");
        }

        if (value.MaxConcurrentExecutionsPerNode <= 0)
        {
            throw new InvalidOperationException("ShedduellerOptions.MaxConcurrentExecutionsPerNode must be positive.");
        }

        if (value.IdlePollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ShedduellerOptions.IdlePollingInterval must be positive.");
        }

        if (this._serviceProvider.GetService<ITaskStore>() is null)
        {
            throw new InvalidOperationException("No Sheddueller task store provider has been registered.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
      => Task.CompletedTask;
}
