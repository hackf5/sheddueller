namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class ReleaseTaskOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ReleaseTask_CurrentLease_RequeuesWithoutRetryConsumptionAndDecrementsGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId, groupKeys: ["shared"]));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.ReleaseTaskAsync(new ReleaseTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Queued");
        task.AttemptCount.ShouldBe(0);
        task.LeaseToken.ShouldBeNull();
        (await context.ReadConcurrencyGroupAsync("shared")).ShouldNotBeNull().InUseCount.ShouldBe(0);
    }

    [Fact]
    public async Task ReleaseTask_StaleToken_ReturnsFalse()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.ReleaseTaskAsync(new ReleaseTaskRequest(taskId, "node-1", Guid.NewGuid(), DateTimeOffset.UtcNow))).ShouldBeFalse();

        (await context.ReadTaskAsync(taskId)).State.ShouldBe("Claimed");
    }
}
