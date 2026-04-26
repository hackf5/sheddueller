#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sheddueller.Dashboard;
using Sheddueller.Dashboard.Internal;
using Sheddueller.Runtime;
using Sheddueller.Storage;

/// <summary>
/// Registration extensions for the embedded Sheddueller dashboard.
/// </summary>
public static class ShedduellerDashboardServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sheddueller dashboard services without exposing routes.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Optional dashboard configuration.</param>
    /// <returns>The same service collection for chained registration.</returns>
    public static IServiceCollection AddShedduellerDashboard(
        this IServiceCollection services,
        Action<ShedduellerDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ShedduellerDashboardOptions>()
          .Configure(options => configure?.Invoke(options))
          .Validate(options => options.EventRetention > TimeSpan.Zero, "ShedduellerDashboardOptions.EventRetention must be positive.")
          .Validate(DashboardTagOrder.IsValid, "ShedduellerDashboardOptions.TagDisplayOrder cannot contain null, empty, or duplicate tag names.")
          .ValidateOnStart();

        services.AddRazorComponents()
          .AddInteractiveServerComponents();
        services.AddSignalR();
        services.TryAddSingleton<DashboardLiveUpdateStream>();
        services.Replace(ServiceDescriptor.Singleton<IJobEventNotifier, SignalRJobEventNotifier>());
        TryAddStartupValidationHostedService(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DashboardJobEventListenerService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobEventRetentionService>());

        return services;
    }

    private static void TryAddStartupValidationHostedService(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShedduellerHostedStartupValidationMarker)))
        {
            return;
        }

        services.AddSingleton<ShedduellerHostedStartupValidationMarker>();
        var descriptor = ServiceDescriptor.Singleton<IHostedService, ShedduellerStartupValidationHostedService>();
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(IHostedService))
            {
                services.Insert(index, descriptor);
                return;
            }
        }

        services.Add(descriptor);
    }
}
