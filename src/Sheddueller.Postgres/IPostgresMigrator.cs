namespace Sheddueller.Postgres;

/// <summary>
/// Applies provider-owned PostgreSQL schema migrations.
/// </summary>
public interface IPostgresMigrator
{
    /// <summary>
    /// Creates or upgrades the configured PostgreSQL schema.
    /// </summary>
    /// <param name="cancellationToken">A token for canceling migration work.</param>
    /// <returns>A task that completes when schema migration has finished.</returns>
    ValueTask ApplyAsync(
        CancellationToken cancellationToken = default);
}
