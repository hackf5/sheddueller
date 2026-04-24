namespace Sheddueller.Postgres.Internal;

using Npgsql;

internal sealed class OwnedPostgresDataSource(
    NpgsqlDataSource dataSource,
    string schemaName) : IDisposable, IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public string SchemaName { get; } = schemaName;

    public void Dispose()
      => this.DataSource.Dispose();

    public ValueTask DisposeAsync()
      => this.DataSource.DisposeAsync();
}
