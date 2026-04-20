namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class MarkTaskCompletedOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MarkCompleted_CurrentLease_CompletesTaskAndDecrementsGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId, groupKeys: ["shared"]));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Completed");
        task.CompletedAtUtc.ShouldNotBeNull();
        (await context.ReadConcurrencyGroupAsync("shared")).ShouldNotBeNull().InUseCount.ShouldBe(0);
    }

    [Fact]
    public async Task MarkCompleted_StaleToken_ReturnsFalseWithoutChangingTask()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(taskId, "node-1", Guid.NewGuid(), DateTimeOffset.UtcNow))).ShouldBeFalse();

        (await context.ReadTaskAsync(taskId)).State.ShouldBe("Claimed");
    }

    [Fact]
    public async Task MarkCompleted_ExpiredLease_ReturnsFalse()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);
        await context.ForceClaimExpiredAsync(taskId);

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeFalse();

        (await context.ReadTaskAsync(taskId)).State.ShouldBe("Claimed");
    }
}
