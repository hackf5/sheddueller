namespace Sheddueller.Tests;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class InMemoryTaskStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryClaimNextAsyncUsesPriorityThenFifoSequence()
    {
        var store = new InMemoryTaskStore();
        var firstLow = Guid.NewGuid();
        var secondLow = Guid.NewGuid();
        var high = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(firstLow, priority: 0));
        await store.EnqueueAsync(CreateRequest(secondLow, priority: 0));
        await store.EnqueueAsync(CreateRequest(high, priority: 10));

        (await ClaimAsync(store)).Task.TaskId.ShouldBe(high);
        (await ClaimAsync(store)).Task.TaskId.ShouldBe(firstLow);
        (await ClaimAsync(store)).Task.TaskId.ShouldBe(secondLow);
    }

    [Fact]
    public async Task TryClaimNextAsyncSkipsBlockedHighPriorityTaskAndClaimsNextEligibleTask()
    {
        var store = new InMemoryTaskStore();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(running, priority: 100, "shared"));
        (await ClaimAsync(store)).Task.TaskId.ShouldBe(running);

        await store.EnqueueAsync(CreateRequest(blocked, priority: 100, "shared"));
        await store.EnqueueAsync(CreateRequest(eligible, priority: 0));

        (await ClaimAsync(store)).Task.TaskId.ShouldBe(eligible);
    }

    [Fact]
    public async Task TryClaimNextAsyncEnforcesClusterWideGroupLimitsAndMultipleGroups()
    {
        var store = new InMemoryTaskStore();
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
        firstClaim.Task.TaskId.ShouldBe(first);
        (await ClaimAsync(store)).Task.TaskId.ShouldBe(second);
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();

        await store.MarkCompletedAsync(new CompleteTaskRequest(first, "node-1", firstClaim.Task.LeaseToken, Now));

        (await ClaimAsync(store)).Task.TaskId.ShouldBe(waitingOnA);
        (await ClaimAsync(store)).Task.TaskId.ShouldBe(waitingOnB);
    }

    [Fact]
    public async Task LoweringLimitBelowOccupancyBlocksFutureClaimsWithoutPreemptingRunningWork()
    {
        var store = new InMemoryTaskStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var waiting = Guid.NewGuid();

        await store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 2, Now));
        await store.EnqueueAsync(CreateRequest(first, priority: 0, "critical"));
        await store.EnqueueAsync(CreateRequest(second, priority: 0, "critical"));
        await store.EnqueueAsync(CreateRequest(waiting, priority: 0, "critical"));

        var firstClaim = await ClaimAsync(store);
        firstClaim.Task.TaskId.ShouldBe(first);
        var secondClaim = await ClaimAsync(store);
        secondClaim.Task.TaskId.ShouldBe(second);

        await store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 1, Now));
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();

        await store.MarkCompletedAsync(new CompleteTaskRequest(first, "node-1", firstClaim.Task.LeaseToken, Now));
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();

        await store.MarkCompletedAsync(new CompleteTaskRequest(second, "node-1", secondClaim.Task.LeaseToken, Now));
        (await ClaimAsync(store)).Task.TaskId.ShouldBe(waiting);
    }

    [Fact]
    public async Task ConcurrentClaimAttemptsCannotClaimTheSameTaskTwice()
    {
        var store = new InMemoryTaskStore();
        await store.EnqueueAsync(CreateRequest(Guid.NewGuid(), priority: 0));

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
          .Select(index => store.TryClaimNextAsync(CreateClaimRequest($"node-{index}")).AsTask()));

        results.Count(result => result is ClaimTaskResult.Claimed).ShouldBe(1);
        results.Count(result => result is ClaimTaskResult.NoTaskAvailable).ShouldBe(19);
    }

    private static async Task<ClaimTaskResult.Claimed> ClaimAsync(InMemoryTaskStore store)
    {
        return (await store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.Claimed>();
    }

    private static ClaimTaskRequest CreateClaimRequest(string nodeId)
      => new(nodeId, Now, Now.AddSeconds(30));

    private static EnqueueTaskRequest CreateRequest(Guid taskId, int priority, params string[] groupKeys)
    {
        return new EnqueueTaskRequest(
          taskId,
          priority,
          typeof(StoreTestService).AssemblyQualifiedName!,
          nameof(StoreTestService.RunAsync),
          [typeof(CancellationToken).AssemblyQualifiedName!],
          new SerializedTaskPayload(SystemTextJsonTaskPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
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
