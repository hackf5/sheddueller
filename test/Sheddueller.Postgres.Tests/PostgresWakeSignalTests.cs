namespace Sheddueller.Postgres.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Runtime;
using Sheddueller.Storage;

public sealed class PostgresWakeSignalTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task WakeSignal_PostgresNotify_WakesWaitingProvider()
    {
        await using var firstContext = await PostgresTestContext.CreateMigratedAsync(fixture);
        await using var secondProvider = PostgresTestContext.CreateProvider(fixture.DataSource, firstContext.SchemaName);
        var waitTask = firstContext.Provider.GetRequiredService<IShedduellerWakeSignal>()
          .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
          .AsTask();

        await secondProvider.GetRequiredService<IJobStore>().EnqueueAsync(PostgresTestData.CreateRequest(Guid.NewGuid()));

        await waitTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
