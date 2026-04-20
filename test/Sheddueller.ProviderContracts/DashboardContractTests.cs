namespace Sheddueller.ProviderContracts;

using Sheddueller.Dashboard;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public abstract class DashboardContractTests
{
    protected abstract ValueTask<DashboardContractContext> CreateContextAsync();

    [Fact]
    public async Task JobTags_ExactPairSearch_FindsTaggedJob()
    {
        await using var context = await this.CreateContextAsync();
        var tagged = Guid.NewGuid();
        var untagged = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          tagged,
          tags:
          [
              new JobTag(" listing_id ", " 23 "),
              new JobTag("listing_id", "23"),
              new JobTag("tenant", "acme"),
          ]));
        await context.Store.EnqueueAsync(CreateRequest(untagged, tags: [new JobTag("listing_id", "24")]));

        var page = await context.Reader.SearchJobsAsync(new DashboardJobQuery(Tag: new JobTag("listing_id", "23")));

        page.Jobs.Select(job => job.TaskId).ShouldBe([tagged]);
        page.Jobs[0].Tags.ShouldBe([new JobTag("listing_id", "23"), new JobTag("tenant", "acme")], ignoreOrder: true);
    }

    [Fact]
    public async Task QueuePosition_ClaimableDelayedBlockedAndMissing_ReportExplicitKinds()
    {
        await using var context = await this.CreateContextAsync();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var delayed = Guid.NewGuid();
        var claimable = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await ClaimAsync(context.Store)).TaskId.ShouldBe(running);
        await context.Store.EnqueueAsync(CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await context.Store.EnqueueAsync(CreateRequest(delayed, notBeforeUtc: DateTimeOffset.UtcNow.AddMinutes(5)));
        await context.Store.EnqueueAsync(CreateRequest(claimable, priority: 50));

        (await context.Reader.GetQueuePositionAsync(claimable)).Kind.ShouldBe(DashboardQueuePositionKind.Claimable);
        (await context.Reader.GetQueuePositionAsync(claimable)).Position.ShouldBe(1);
        (await context.Reader.GetQueuePositionAsync(blocked)).Kind.ShouldBe(DashboardQueuePositionKind.BlockedByConcurrency);
        (await context.Reader.GetQueuePositionAsync(delayed)).Kind.ShouldBe(DashboardQueuePositionKind.Delayed);
        (await context.Reader.GetQueuePositionAsync(Guid.NewGuid())).Kind.ShouldBe(DashboardQueuePositionKind.NotFound);
    }

    [Fact]
    public async Task Events_AppendReadLatestProgressAndCleanup_RoundTrips()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(taskId));
        var logEvent = await context.EventSink.AppendAsync(new AppendDashboardJobEventRequest(
          taskId,
          DashboardJobEventKind.Log,
          AttemptNumber: 0,
          LogLevel: JobLogLevel.Information,
          Message: "starting",
          Fields: new Dictionary<string, string>(StringComparer.Ordinal) { ["step"] = "one" }));
        var progressEvent = await context.EventSink.AppendAsync(new AppendDashboardJobEventRequest(
          taskId,
          DashboardJobEventKind.Progress,
          AttemptNumber: 0,
          Message: "half",
          ProgressPercent: 50));

        var events = await ReadAllAsync(context.Reader, taskId);
        var sequences = events.Select(jobEvent => jobEvent.EventSequence).ToArray();
        sequences.SequenceEqual(sequences.Order()).ShouldBeTrue();
        logEvent.EventSequence.ShouldBeGreaterThan(0);
        progressEvent.EventSequence.ShouldBeGreaterThan(logEvent.EventSequence);

        var detail = await context.Reader.GetJobAsync(taskId);
        detail.ShouldNotBeNull();
        detail.Summary.LatestProgress.ShouldNotBeNull().Percent.ShouldBe(50);

        var claimed = await ClaimAsync(context.Store);
        await context.Store.MarkCompletedAsync(new CompleteTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        (await context.RetentionStore.CleanupAsync(TimeSpan.FromMilliseconds(1))).ShouldBeGreaterThan(0);
        (await ReadAllAsync(context.Reader, taskId)).ShouldBeEmpty();
    }

    protected static async ValueTask<ClaimedTask> ClaimAsync(ITaskStore store)
      => (await store.TryClaimNextAsync(new ClaimTaskRequest("node-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30))))
        .ShouldBeOfType<ClaimTaskResult.Claimed>()
        .Task;

    protected static EnqueueTaskRequest CreateRequest(
        Guid taskId,
        int priority = 0,
        DateTimeOffset? notBeforeUtc = null,
        IReadOnlyList<string>? groupKeys = null,
        IReadOnlyList<JobTag>? tags = null)
      => new(
        taskId,
        priority,
        typeof(DashboardContractService).AssemblyQualifiedName!,
        nameof(DashboardContractService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        new SerializedTaskPayload(SystemTextJsonTaskPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
        groupKeys ?? [],
        DateTimeOffset.UtcNow,
        notBeforeUtc,
        Tags: tags);

    private static async ValueTask<IReadOnlyList<DashboardJobEvent>> ReadAllAsync(IDashboardJobReader reader, Guid taskId)
    {
        var events = new List<DashboardJobEvent>();
        await foreach (var jobEvent in reader.ReadEventsAsync(taskId))
        {
            events.Add(jobEvent);
        }

        return events;
    }

    private sealed class DashboardContractService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
