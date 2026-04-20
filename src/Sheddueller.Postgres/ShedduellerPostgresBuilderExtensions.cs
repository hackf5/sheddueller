#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sheddueller.DependencyInjection;
using Sheddueller.Postgres;
using Sheddueller.Postgres.Internal;
using Sheddueller.Runtime;
using Sheddueller.Storage;

/// <summary>
/// Registration extensions for the PostgreSQL Sheddueller provider.
/// </summary>
public static class ShedduellerPostgresBuilderExtensions
{
    /// <summary>
    /// Uses the PostgreSQL task store provider.
    /// </summary>
    public static ShedduellerBuilder UsePostgres(
        this ShedduellerBuilder builder,
        Action<ShedduellerPostgresOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ShedduellerPostgresOptions { DataSource = null! };
        configure(options);
        PostgresOptionsValidator.Validate(options);

        builder.Services.Replace(ServiceDescriptor.Singleton(options));
        builder.Services.Replace(ServiceDescriptor.Singleton<ITaskStore, PostgresTaskStore>());
        builder.Services.Replace(ServiceDescriptor.Singleton<IPostgresMigrator, PostgresMigrator>());
        builder.Services.Replace(ServiceDescriptor.Singleton<IShedduellerWakeSignal, PostgresWakeSignal>());
        InsertStartupValidatorBeforeWorker(builder.Services);

        return builder;
    }

    private static void InsertStartupValidatorBeforeWorker(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)
          && descriptor.ImplementationType == typeof(PostgresStartupValidator)))
        {
            return;
        }

        var descriptor = ServiceDescriptor.Singleton<IHostedService, PostgresStartupValidator>();
        var workerIndex = services
          .Select((serviceDescriptor, index) => (serviceDescriptor, index))
          .FirstOrDefault(item => item.serviceDescriptor.ServiceType == typeof(IHostedService)
            && item.serviceDescriptor.ImplementationType == typeof(ShedduellerWorker))
          .index;

        if (workerIndex > 0)
        {
            services.Insert(workerIndex, descriptor);
            return;
        }

        services.Add(descriptor);
    }
}
