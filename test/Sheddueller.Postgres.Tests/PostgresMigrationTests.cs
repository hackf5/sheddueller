namespace Sheddueller.Postgres.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Postgres;

using Shouldly;

public sealed class PostgresMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migration_FreshSchema_CreatesSchemaAndStampsVersion()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        (await context.ReadSchemaVersionAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Migration_Reapplied_IsIdempotent()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var migrator = context.Provider.GetRequiredService<IPostgresMigrator>();

        await migrator.ApplyAsync();

        (await context.ReadSchemaVersionAsync()).ShouldBe(2);
    }
}
