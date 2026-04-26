namespace Sheddueller.Postgres;

using Npgsql;

/// <summary>
/// Options for a Sheddueller-owned PostgreSQL data source.
/// </summary>
/// <remarks>
/// These options are used by connection-string registration overloads. Use
/// <see cref="ShedduellerPostgresOptions"/> when supplying a caller-owned data source.
/// </remarks>
public sealed class ShedduellerPostgresDataSourceOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL schema used by one logical Sheddueller cluster.
    /// </summary>
    public string SchemaName { get; set; } = "sheddueller";

    /// <summary>
    /// Gets or sets a callback for configuring the underlying Npgsql data source builder.
    /// </summary>
    public Action<NpgsqlDataSourceBuilder>? ConfigureDataSourceBuilder { get; set; }
}
