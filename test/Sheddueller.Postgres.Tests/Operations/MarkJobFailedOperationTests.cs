namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class MarkTaskFailedOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MarkFailed_NoRetryPolicy_FailsTerminallyAndDecrementsGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId, groupKeys: ["shared"]));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, PostgresTestData.CreateFailure()))).ShouldBeTrue();

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Failed");
        task.FailedAtUtc.ShouldNotBeNull();
        task.FailureTypeName.ShouldBe("TestException");
        task.FailureMessage.ShouldBe("failed");
        task.FailureStackTrace.ShouldBe("stack");
        (await context.ReadConcurrencyGroupAsync("shared")).ShouldNotBeNull().InUseCount.ShouldBe(0);
    }

    [Fact]
    public async Task MarkFailed_RetryPolicy_RequeuesWithRetryDelay()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(
          taskId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(500)));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, PostgresTestData.CreateFailure()))).ShouldBeTrue();

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Queued");
        task.AttemptCount.ShouldBe(1);
        task.NotBeforeUtc.ShouldNotBeNull();
        task.LeaseToken.ShouldBeNull();
    }

    [Fact]
    public async Task MarkFailed_StaleToken_ReturnsFalse()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", Guid.NewGuid(), DateTimeOffset.UtcNow, PostgresTestData.CreateFailure()))).ShouldBeFalse();

        (await context.ReadTaskAsync(taskId)).State.ShouldBe("Claimed");
    }
}
