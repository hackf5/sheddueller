namespace Sheddueller.Postgres;

/// <summary>
/// Applies provider-owned PostgreSQL schema migrations.
/// </summary>
public interface IPostgresMigrator
{
    /// <summary>
    /// Creates or upgrades the configured PostgreSQL schema.
    /// </summary>
    ValueTask ApplyAsync(
        CancellationToken cancellationToken = default);
}
