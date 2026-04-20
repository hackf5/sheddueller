namespace Sheddueller.Tests;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class InMemoryJobStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryClaimNext_PriorityAndFifo_ClaimsHigherPriorityThenOldest()
    {
        var store = new InMemoryJobStore();
        var firstLow = Guid.NewGuid();
        var secondLow = Guid.NewGuid();
        var high = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(firstLow, priority: 0));
        await store.EnqueueAsync(CreateRequest(secondLow, priority: 0));
        await store.EnqueueAsync(CreateRequest(high, priority: 10));

        (await ClaimAsync(store)).Job.JobId.ShouldBe(high);
        (await ClaimAsync(store)).Job.JobId.ShouldBe(firstLow);
        (await ClaimAsync(store)).Job.JobId.ShouldBe(secondLow);
    }

    [Fact]
    public async Task TryClaimNext_BlockedHighPriorityTask_ClaimsNextEligibleTask()
    {
        var store = new InMemoryJobStore();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(running, priority: 100, "shared"));
        (await ClaimAsync(store)).Job.JobId.ShouldBe(running);

        await store.EnqueueAsync(CreateRequest(blocked, priority: 100, "shared"));
        await store.EnqueueAsync(CreateRequest(eligible, priority: 0));

        (await ClaimAsync(store)).Job.JobId.ShouldBe(eligible);
    }

    [Fact]
    public async Task TryClaimNext_ConcurrencyGroups_EnforcesClusterWideLimits()
    {
        var store = new InMemoryJobStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var waitingOnA = Guid.NewGuid();
        var waitingOnB = Guid.NewGuid();

        await store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("a", 2, Now));
        await store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("b", 1, Now));
        await store.EnqueueAsync(CreateRequest(first, priority: 0, "a", "b"));
        await store.EnqueueAsync(CreateRequest(second, priority: 0, "a"));
        await store.EnqueueAsync(CreateRequest(waitingOnA, priority: 0, "a"));
        await store.EnqueueAsync(CreateRequest(waitingOnB, priority: 0, "b"));

        var firstClaim = await ClaimAsync(store);
        firstClaim.Job.JobId.ShouldBe(first);
        (await ClaimAsync(store)).Job.JobId.ShouldBe(second);
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        await store.MarkCompletedAsync(new CompleteJobRequest(first, "node-1", firstClaim.Job.LeaseToken, Now));

        (await ClaimAsync(store)).Job.JobId.ShouldBe(waitingOnA);
        (await ClaimAsync(store)).Job.JobId.ShouldBe(waitingOnB);
    }

    [Fact]
    public async Task ConcurrencyLimit_LoweredBelowOccupancy_BlocksFutureClaimsWithoutPreemptingRunningWork()
    {
        var store = new InMemoryJobStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var waiting = Guid.NewGuid();

        await store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 2, Now));
        await store.EnqueueAsync(CreateRequest(first, priority: 0, "critical"));
        await store.EnqueueAsync(CreateRequest(second, priority: 0, "critical"));
        await store.EnqueueAsync(CreateRequest(waiting, priority: 0, "critical"));

        var firstClaim = await ClaimAsync(store);
        firstClaim.Job.JobId.ShouldBe(first);
        var secondClaim = await ClaimAsync(store);
        secondClaim.Job.JobId.ShouldBe(second);

        await store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 1, Now));
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        await store.MarkCompletedAsync(new CompleteJobRequest(first, "node-1", firstClaim.Job.LeaseToken, Now));
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        await store.MarkCompletedAsync(new CompleteJobRequest(second, "node-1", secondClaim.Job.LeaseToken, Now));
        (await ClaimAsync(store)).Job.JobId.ShouldBe(waiting);
    }

    [Fact]
    public async Task TryClaimNext_ConcurrentAttempts_ClaimsJobOnlyOnce()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(CreateRequest(Guid.NewGuid(), priority: 0));

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
          .Select(index => store.TryClaimNextAsync(CreateClaimRequest($"node-{index}")).AsTask()));

        results.Count(result => result is ClaimJobResult.Claimed).ShouldBe(1);
        results.Count(result => result is ClaimJobResult.NoJobAvailable).ShouldBe(19);
    }

    private static async Task<ClaimJobResult.Claimed> ClaimAsync(InMemoryJobStore store)
    {
        return (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.Claimed>();
    }

    private static ClaimJobRequest CreateClaimRequest(string nodeId)
      => new(nodeId, Now, Now.AddSeconds(30));

    private static EnqueueJobRequest CreateRequest(Guid jobId, int priority, params string[] groupKeys)
    {
        return new EnqueueJobRequest(
          jobId,
          priority,
          typeof(StoreTestService).AssemblyQualifiedName!,
          nameof(StoreTestService.RunAsync),
          [typeof(CancellationToken).AssemblyQualifiedName!],
          new SerializedJobPayload(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
          groupKeys,
          Now);
    }

    private sealed class StoreTestService
    {
        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
