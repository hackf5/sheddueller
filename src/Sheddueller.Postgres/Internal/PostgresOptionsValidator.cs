namespace Sheddueller.Postgres.Internal;

internal static class PostgresOptionsValidator
{
    public static void Validate(ShedduellerPostgresOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DataSource is null)
        {
            throw new InvalidOperationException("ShedduellerPostgresOptions.DataSource is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SchemaName))
        {
            throw new InvalidOperationException("ShedduellerPostgresOptions.SchemaName must be a non-empty PostgreSQL schema name.");
        }

        if (options.SchemaName.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ShedduellerPostgresOptions.SchemaName cannot contain null characters.");
        }
    }
}
