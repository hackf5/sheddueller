namespace Sheddueller.Postgres.Tests.ProviderContracts;

using Sheddueller.ProviderContracts;

public sealed class PostgresTaskStoreContractTests(PostgresFixture fixture) : TaskStoreContractTests, IClassFixture<PostgresFixture>
{
    protected override async ValueTask<TaskStoreContractContext> CreateContextAsync()
    {
        var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        return new TaskStoreContractContext(context.Store, context.MakeScheduleDueAsync, context);
    }
}
