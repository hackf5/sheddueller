namespace Sheddueller.Postgres;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Convenience extensions for applying Sheddueller PostgreSQL migrations explicitly.
/// </summary>
public static class ShedduellerPostgresMigrationExtensions
{
    /// <summary>
    /// Applies provider-owned PostgreSQL schema migrations through the host service provider.
    /// </summary>
    /// <param name="host">The host whose service provider contains the PostgreSQL provider.</param>
    /// <param name="cancellationToken">A token for canceling migration work.</param>
    /// <returns>A task that completes when schema migration has finished.</returns>
    public static ValueTask ApplyShedduellerPostgresMigrationsAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.Services.ApplyShedduellerPostgresMigrationsAsync(cancellationToken);
    }

    /// <summary>
    /// Applies provider-owned PostgreSQL schema migrations through a scoped migrator.
    /// </summary>
    /// <param name="services">The service provider containing the PostgreSQL provider.</param>
    /// <param name="cancellationToken">A token for canceling migration work.</param>
    /// <returns>A task that completes when schema migration has finished.</returns>
    public static async ValueTask ApplyShedduellerPostgresMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IPostgresMigrator>();
        await migrator.ApplyAsync(cancellationToken).ConfigureAwait(false);
    }
}
