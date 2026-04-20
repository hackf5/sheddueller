namespace Sheddueller.Postgres.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

public sealed class PostgresStartupValidationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task StartupValidation_MissingSchema_FailsBeforeWorkerStarts()
    {
        var schemaName = "sheddueller_" + Guid.NewGuid().ToString("N");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSheddueller(sheddueller => sheddueller.UsePostgres(options =>
        {
            options.DataSource = fixture.DataSource;
            options.SchemaName = schemaName;
        }));
        using var host = builder.Build();

        await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync());
    }
}
