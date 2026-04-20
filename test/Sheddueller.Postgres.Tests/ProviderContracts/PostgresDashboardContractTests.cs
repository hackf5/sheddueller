namespace Sheddueller.Postgres.Tests.ProviderContracts;

using Sheddueller.Dashboard;
using Sheddueller.ProviderContracts;

public sealed class PostgresDashboardContractTests(PostgresFixture fixture) : DashboardContractTests, IClassFixture<PostgresFixture>
{
    protected override async ValueTask<DashboardContractContext> CreateContextAsync()
    {
        var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var reader = (IDashboardJobReader)context.Store;
        var eventSink = (IDashboardEventSink)context.Store;
        var retentionStore = (IDashboardEventRetentionStore)context.Store;

        return new DashboardContractContext(context.Store, reader, eventSink, retentionStore, context);
    }
}
