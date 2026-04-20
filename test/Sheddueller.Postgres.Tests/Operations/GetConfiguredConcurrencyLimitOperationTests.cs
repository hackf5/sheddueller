namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class GetConfiguredConcurrencyLimitOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetConfiguredConcurrencyLimit_MissingGroup_ReturnsNull()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        (await context.Store.GetConfiguredConcurrencyLimitAsync("missing")).ShouldBeNull();
    }

    [Fact]
    public async Task GetConfiguredConcurrencyLimit_ConfiguredGroup_ReturnsLimit()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("shared", 4, DateTimeOffset.UtcNow));

        (await context.Store.GetConfiguredConcurrencyLimitAsync("shared")).ShouldBe(4);
    }
}
