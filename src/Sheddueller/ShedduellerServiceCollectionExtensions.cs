#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Sheddueller;
using Sheddueller.Enqueueing;
using Sheddueller.Runtime;
using Sheddueller.Serialization;
using Sheddueller.Storage;

/// <summary>
/// Registration extensions for Sheddueller.
/// </summary>
public static class ShedduellerServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sheddueller services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Optional Sheddueller configuration, including storage provider registration.</param>
    /// <returns>The same service collection for chained registration.</returns>
    public static IServiceCollection AddSheddueller(
        this IServiceCollection services,
        Action<ShedduellerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddOptions<ShedduellerOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJobPayloadSerializer, SystemTextJsonJobPayloadSerializer>();
        services.TryAddSingleton<IJobEnqueuer, JobEnqueuer>();
        services.TryAddSingleton<IJobManager, JobManager>();
        services.TryAddSingleton<IRecurringScheduleManager, RecurringScheduleManager>();
        services.TryAddSingleton<IConcurrencyGroupManager, ConcurrencyGroupManager>();
        services.TryAddSingleton<IShedduellerWakeSignal, ShedduellerWakeSignal>();
        services.TryAddSingleton<IJobEventSink, NoOpJobEventSink>();
        services.TryAddSingleton<IJobEventNotifier, NoOpJobEventNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShedduellerStartupValidator, ShedduellerCommonStartupValidator>());

        configure?.Invoke(new ShedduellerBuilder(services));

        return services;
    }
}
