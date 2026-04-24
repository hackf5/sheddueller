#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sheddueller;
using Sheddueller.Runtime;
using Sheddueller.Worker.Internal;

/// <summary>
/// Registration extensions for Sheddueller worker execution.
/// </summary>
public static class ShedduellerWorkerServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sheddueller client services and starts a worker execution loop.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Optional Sheddueller configuration, including storage provider registration.</param>
    /// <returns>The same service collection for chained registration.</returns>
    public static IServiceCollection AddShedduellerWorker(
        this IServiceCollection services,
        Action<ShedduellerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSheddueller(configure);
        services.TryAddSingleton<IShedduellerNodeIdProvider, ShedduellerNodeIdProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShedduellerStartupValidator, ShedduellerWorkerStartupValidator>());
        TryAddStartupValidationHostedService(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ShedduellerWorker>());

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
