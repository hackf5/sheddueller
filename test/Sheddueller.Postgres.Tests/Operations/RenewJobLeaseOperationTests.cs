namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class RenewTaskLeaseOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RenewLease_CurrentLease_UpdatesHeartbeatAndExpiry()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);
        var heartbeatAt = DateTimeOffset.UtcNow;

        (await context.Store.RenewLeaseAsync(new RenewLeaseRequest(taskId, "node-1", claimed.LeaseToken, heartbeatAt, heartbeatAt.AddMinutes(2)))).ShouldBeTrue();

        var task = await context.ReadTaskAsync(taskId);
        task.LastHeartbeatAtUtc.ShouldNotBeNull();
        task.LeaseExpiresAtUtc.ShouldNotBeNull();
        task.LeaseExpiresAtUtc.Value.ShouldBeGreaterThan(claimed.LeaseExpiresAtUtc);
    }

    [Fact]
    public async Task RenewLease_StaleToken_ReturnsFalse()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId));
        await PostgresTestData.ClaimAsync(context.Store);
        var heartbeatAt = DateTimeOffset.UtcNow;

        (await context.Store.RenewLeaseAsync(new RenewLeaseRequest(taskId, "node-1", Guid.NewGuid(), heartbeatAt, heartbeatAt.AddMinutes(1)))).ShouldBeFalse();
    }

    [Fact]
    public async Task RenewLease_InvalidLeaseDuration_Throws()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var now = DateTimeOffset.UtcNow;

        await Should.ThrowAsync<ArgumentException>(() =>
          context.Store.RenewLeaseAsync(new RenewLeaseRequest(Guid.NewGuid(), "node-1", Guid.NewGuid(), now, now)).AsTask());
    }
}
