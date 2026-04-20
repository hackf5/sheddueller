namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class CancelTaskOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Cancel_QueuedTask_MarksCanceled()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));

        (await context.Store.CancelAsync(new CancelTaskRequest(taskId, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Canceled");
        task.CanceledAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Cancel_ClaimedOrMissingTask_ReturnsFalse()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.CancelAsync(new CancelTaskRequest(taskId, DateTimeOffset.UtcNow))).ShouldBeFalse();
        (await context.Store.CancelAsync(new CancelTaskRequest(Guid.NewGuid(), DateTimeOffset.UtcNow))).ShouldBeFalse();
    }
}
