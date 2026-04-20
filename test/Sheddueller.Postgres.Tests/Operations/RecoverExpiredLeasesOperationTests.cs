namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class RecoverExpiredLeasesOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RecoverExpiredLeases_NoExpiredClaims_ReturnsZero()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(Guid.NewGuid()));

        (await context.Store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(DateTimeOffset.UtcNow))).ShouldBe(0);
    }

    [Fact]
    public async Task RecoverExpiredLeases_ExpiredClaimWithRetry_RequeuesAndDecrementsGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(
          taskId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(1),
          groupKeys: ["shared"]));
        await PostgresTestData.ClaimAsync(context.Store);
        await context.ForceClaimExpiredAsync(taskId);

        (await context.Store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(DateTimeOffset.UtcNow))).ShouldBe(1);

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Queued");
        task.AttemptCount.ShouldBe(1);
        task.NotBeforeUtc.ShouldNotBeNull();
        (await context.ReadConcurrencyGroupAsync("shared")).ShouldNotBeNull().InUseCount.ShouldBe(0);
    }

    [Fact]
    public async Task RecoverExpiredLeases_ExpiredClaimWithoutRetry_FailsTerminally()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        await PostgresTestData.ClaimAsync(context.Store);
        await context.ForceClaimExpiredAsync(taskId);

        (await context.Store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(DateTimeOffset.UtcNow))).ShouldBe(1);

        var task = await context.ReadTaskAsync(taskId);
        task.State.ShouldBe("Failed");
        task.FailureTypeName.ShouldBe("Sheddueller.LeaseExpired");
    }
}
