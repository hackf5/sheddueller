namespace Sheddueller.Postgres;

using Npgsql;

/// <summary>
/// PostgreSQL provider options.
/// </summary>
public sealed class ShedduellerPostgresOptions
{
    /// <summary>
    /// Gets or sets the caller-owned data source used by the provider.
    /// </summary>
    public required NpgsqlDataSource DataSource { get; set; }

    /// <summary>
    /// Gets or sets the PostgreSQL schema used by one logical Sheddueller cluster.
    /// </summary>
    public string SchemaName { get; set; } = "sheddueller";
}
