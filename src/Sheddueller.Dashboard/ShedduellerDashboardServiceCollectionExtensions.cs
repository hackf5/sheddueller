#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sheddueller.Dashboard;
using Sheddueller.Dashboard.Internal;

/// <summary>
/// Registration extensions for the embedded Sheddueller dashboard.
/// </summary>
public static class ShedduellerDashboardServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sheddueller dashboard services without exposing routes.
    /// </summary>
    public static IServiceCollection AddShedduellerDashboard(
        this IServiceCollection services,
        Action<ShedduellerDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ShedduellerDashboardOptions>()
          .Configure(options => configure?.Invoke(options))
          .Validate(options => options.EventRetention > TimeSpan.Zero, "ShedduellerDashboardOptions.EventRetention must be positive.")
          .ValidateOnStart();

        services.AddRazorComponents()
          .AddInteractiveServerComponents();
        services.AddSignalR();
        services.TryAddSingleton<DashboardLiveUpdateStream>();
        services.Replace(ServiceDescriptor.Singleton<IDashboardLiveUpdatePublisher, SignalRDashboardLiveUpdatePublisher>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DashboardEventRetentionService>());

        return services;
    }
}
