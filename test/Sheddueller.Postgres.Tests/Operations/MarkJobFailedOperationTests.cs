namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class MarkJobFailedOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MarkFailed_NoRetryPolicy_FailsTerminallyAndDecrementsGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(jobId, groupKeys: ["shared"]));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, PostgresTestData.CreateFailure()))).ShouldBeTrue();

        var job = await context.ReadJobAsync(jobId);
        job.State.ShouldBe("Failed");
        job.FailedAtUtc.ShouldNotBeNull();
        job.FailureTypeName.ShouldBe("TestException");
        job.FailureMessage.ShouldBe("failed");
        job.FailureStackTrace.ShouldBe("stack");
        (await context.ReadConcurrencyGroupAsync("shared")).ShouldNotBeNull().InUseCount.ShouldBe(0);
    }

    [Fact]
    public async Task MarkFailed_RetryPolicy_RequeuesWithRetryDelay()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(
          jobId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(500)));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, PostgresTestData.CreateFailure()))).ShouldBeTrue();

        var job = await context.ReadJobAsync(jobId);
        job.State.ShouldBe("Queued");
        job.AttemptCount.ShouldBe(1);
        job.NotBeforeUtc.ShouldNotBeNull();
        job.LeaseToken.ShouldBeNull();
    }

    [Fact]
    public async Task MarkFailed_StaleToken_ReturnsFalse()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(jobId));
        await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", Guid.NewGuid(), DateTimeOffset.UtcNow, PostgresTestData.CreateFailure()))).ShouldBeFalse();

        (await context.ReadJobAsync(jobId)).State.ShouldBe("Claimed");
    }
}
