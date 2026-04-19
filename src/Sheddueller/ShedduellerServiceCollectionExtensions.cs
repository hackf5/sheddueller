namespace Sheddueller;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Registration extensions for Sheddueller.
/// </summary>
public static class ShedduellerServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sheddueller services to the service collection.
    /// </summary>
    public static IServiceCollection AddSheddueller(
      this IServiceCollection services,
      Action<ShedduellerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ShedduellerOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITaskPayloadSerializer, SystemTextJsonTaskPayloadSerializer>();
        services.TryAddSingleton<ITaskEnqueuer, TaskEnqueuer>();
        services.TryAddSingleton<IConcurrencyGroupManager, ConcurrencyGroupManager>();
        services.TryAddSingleton<IShedduellerWakeSignal, ShedduellerWakeSignal>();
        services.TryAddSingleton<IShedduellerNodeIdProvider, ShedduellerNodeIdProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ShedduellerStartupValidator>());
        services.AddHostedService<ShedduellerWorker>();

        configure?.Invoke(new ShedduellerBuilder(services));

        return services;
    }
}

/// <summary>
/// Host application builder extensions for Sheddueller.
/// </summary>
public static class ShedduellerHostApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Sheddueller services to a host application builder.
    /// </summary>
    public static HostApplicationBuilder AddSheddueller(
      this HostApplicationBuilder builder,
      Action<ShedduellerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSheddueller(configure);
        return builder;
    }
}
