namespace Sheddueller.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Sheddueller.DependencyInjection;
using Sheddueller.Enqueueing;
using Sheddueller.Storage;

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

        if (value.LeaseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ShedduellerOptions.LeaseDuration must be positive.");
        }

        if (value.HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ShedduellerOptions.HeartbeatInterval must be positive.");
        }

        if (value.HeartbeatInterval >= value.LeaseDuration)
        {
            throw new InvalidOperationException("ShedduellerOptions.HeartbeatInterval must be less than LeaseDuration.");
        }

        try
        {
            SubmissionValidator.ValidateRetryPolicy(value.DefaultRetryPolicy);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("ShedduellerOptions.DefaultRetryPolicy is invalid.", exception);
        }

        if (this._serviceProvider.GetService<IJobStore>() is null)
        {
            throw new InvalidOperationException("No Sheddueller job store provider has been registered.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
      => Task.CompletedTask;
}
