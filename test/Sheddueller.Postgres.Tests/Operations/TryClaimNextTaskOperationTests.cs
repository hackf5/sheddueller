namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class TryClaimNextTaskOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TryClaim_NoQueuedTasks_ReturnsNoTaskAvailable()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        (await context.Store.TryClaimNextAsync(PostgresTestData.ClaimRequest())).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
    }

    [Fact]
    public async Task TryClaim_InvalidLeaseDuration_Throws()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var now = DateTimeOffset.UtcNow;

        await Should.ThrowAsync<ArgumentException>(() =>
          context.Store.TryClaimNextAsync(new ClaimTaskRequest("node-1", now, now)).AsTask());
    }

    [Fact]
    public async Task TryClaim_PriorityAndFifo_ClaimsHigherPriorityThenOldest()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var firstLow = Guid.NewGuid();
        var secondLow = Guid.NewGuid();
        var high = Guid.NewGuid();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(firstLow, priority: 0));
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(secondLow, priority: 0));
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(high, priority: 10));

        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(high);
        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(firstLow);
        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(secondLow);
    }

    [Fact]
    public async Task TryClaim_FutureNotBefore_IsNotClaimableUntilDatabaseTimePasses()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId, notBeforeUtc: DateTimeOffset.UtcNow.AddMilliseconds(250)));

        (await context.Store.TryClaimNextAsync(PostgresTestData.ClaimRequest())).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(taskId);
    }

    [Fact]
    public async Task TryClaim_ReservedGroups_IncrementsInUseCountAndReturnsClaim()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(taskId, groupKeys: ["a", "b"]));

        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        claimed.TaskId.ShouldBe(taskId);
        (await context.ReadConcurrencyGroupAsync("a")).ShouldNotBeNull().InUseCount.ShouldBe(1);
        (await context.ReadConcurrencyGroupAsync("b")).ShouldNotBeNull().InUseCount.ShouldBe(1);
    }

    [Fact]
    public async Task TryClaim_SaturatedGroup_ClaimsNextEligibleTask()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(running);

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(eligible, priority: 0));

        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(eligible);
        (await context.ReadTaskAsync(blocked)).State.ShouldBe("Queued");
    }

    [Fact]
    public async Task TryClaim_SaturatedGroupPrefixBeyondOldCandidateLimit_ClaimsLaterEligibleTask()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var running = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(running);

        for (var i = 0; i < 64; i++)
        {
            await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(Guid.NewGuid(), priority: 100, groupKeys: ["shared"]));
        }

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(eligible, priority: 0));

        (await PostgresTestData.ClaimAsync(context.Store)).TaskId.ShouldBe(eligible);
    }

    [Fact]
    public async Task TryClaim_ConcurrentNodes_ClaimsTaskOnlyOnce()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(Guid.NewGuid()));

        var results = await Task.WhenAll(Enumerable.Range(0, 2)
          .Select(index => context.Store.TryClaimNextAsync(PostgresTestData.ClaimRequest($"node-{index}")).AsTask()));

        results.Count(result => result is ClaimTaskResult.Claimed).ShouldBe(1);
        results.Count(result => result is ClaimTaskResult.NoTaskAvailable).ShouldBe(1);
    }
}
