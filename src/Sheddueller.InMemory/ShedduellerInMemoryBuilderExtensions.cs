#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Sheddueller;
using Sheddueller.Dashboard;
using Sheddueller.DependencyInjection;
using Sheddueller.Storage;

/// <summary>
/// Registration extensions for the in-memory Sheddueller provider.
/// </summary>
public static class ShedduellerInMemoryBuilderExtensions
{
    /// <summary>
    /// Uses the process-local in-memory task store.
    /// </summary>
    public static ShedduellerBuilder UseInMemoryStore(this ShedduellerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton<InMemoryTaskStore, InMemoryTaskStore>());
        builder.Services.Replace(ServiceDescriptor.Singleton<ITaskStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryTaskStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IDashboardJobReader>(serviceProvider => serviceProvider.GetRequiredService<InMemoryTaskStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IDashboardEventSink>(serviceProvider => serviceProvider.GetRequiredService<InMemoryTaskStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IDashboardEventRetentionStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryTaskStore>()));
        return builder;
    }
}
