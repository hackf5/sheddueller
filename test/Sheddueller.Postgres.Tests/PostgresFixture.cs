namespace Sheddueller.Postgres.Tests;

using Npgsql;

using Testcontainers.PostgreSql;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var image = Environment.GetEnvironmentVariable("SHEDDUELLER_POSTGRES_IMAGE");
        if (string.IsNullOrWhiteSpace(image))
        {
            image = "postgres:14";
        }

        this._container = new PostgreSqlBuilder(image)
          .WithDatabase("sheddueller_tests")
          .WithUsername("postgres")
          .WithPassword("postgres")
          .Build();

        await this._container.StartAsync();
        this.DataSource = NpgsqlDataSource.Create(this._container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        if (this.DataSource is not null)
        {
            await this.DataSource.DisposeAsync();
        }

        if (this._container is not null)
        {
            await this._container.DisposeAsync();
        }
    }
}
