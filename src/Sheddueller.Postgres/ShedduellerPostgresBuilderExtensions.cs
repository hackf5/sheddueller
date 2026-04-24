#pragma warning disable IDE0130

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Sheddueller;
using Sheddueller.Inspection.ConcurrencyGroups;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Inspection.Metrics;
using Sheddueller.Inspection.Nodes;
using Sheddueller.Inspection.Schedules;
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
    /// Uses the PostgreSQL job store provider.
    /// </summary>
    /// <param name="builder">The Sheddueller builder being configured.</param>
    /// <param name="configure">The PostgreSQL provider options callback.</param>
    /// <returns>The same builder for chained configuration.</returns>
    /// <remarks>
    /// The configured <see cref="ShedduellerPostgresOptions.DataSource"/> is caller-owned. Apply provider
    /// migrations explicitly with <see cref="IPostgresMigrator"/> before starting workers against a new schema.
    /// </remarks>
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
        builder.Services.Replace(ServiceDescriptor.Singleton<PostgresJobStore, PostgresJobStore>());
        builder.Services.Replace(ServiceDescriptor.Singleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IJobInspectionReader>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IJobEventSink>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IJobEventRetentionStore>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IScheduleInspectionReader>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IConcurrencyGroupInspectionReader>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<INodeInspectionReader>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IMetricsInspectionReader>(serviceProvider => serviceProvider.GetRequiredService<PostgresJobStore>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IPostgresMigrator, PostgresMigrator>());
        builder.Services.Replace(ServiceDescriptor.Singleton<IShedduellerWakeSignal, PostgresWakeSignal>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IShedduellerStartupValidator, PostgresStartupValidator>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IShedduellerJobEventListener, PostgresJobEventListener>());

        return builder;
    }
}
