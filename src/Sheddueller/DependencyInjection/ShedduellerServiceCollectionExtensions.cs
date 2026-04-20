#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sheddueller;
using Sheddueller.Dashboard;
using Sheddueller.DependencyInjection;
using Sheddueller.Enqueueing;
using Sheddueller.Runtime;
using Sheddueller.Serialization;

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
        services.TryAddSingleton<ITaskManager, TaskManager>();
        services.TryAddSingleton<IRecurringScheduleManager, RecurringScheduleManager>();
        services.TryAddSingleton<IConcurrencyGroupManager, ConcurrencyGroupManager>();
        services.TryAddSingleton<IShedduellerWakeSignal, ShedduellerWakeSignal>();
        services.TryAddSingleton<IShedduellerNodeIdProvider, ShedduellerNodeIdProvider>();
        services.TryAddSingleton<IDashboardEventSink, NoOpDashboardEventSink>();
        services.TryAddSingleton<IDashboardLiveUpdatePublisher, NoOpDashboardLiveUpdatePublisher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ShedduellerStartupValidator>());
        services.AddHostedService<ShedduellerWorker>();

        configure?.Invoke(new ShedduellerBuilder(services));

        return services;
    }
}
