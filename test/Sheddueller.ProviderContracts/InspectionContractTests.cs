namespace Sheddueller.ProviderContracts;

using System.Text.Json;

using Sheddueller.Inspection.ConcurrencyGroups;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Inspection.Metrics;
using Sheddueller.Inspection.Nodes;
using Sheddueller.Inspection.Schedules;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public abstract class InspectionContractTests
{
    protected abstract ValueTask<InspectionContractContext> CreateContextAsync();

    [Fact]
    public async Task SearchJobs_TagSubstringSearch_FindsTaggedJob()
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

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(TagContains: "LISTING_ID:23"));

        page.Jobs.Select(job => job.JobId).ShouldBe([tagged]);
        page.Jobs[0].Tags.ShouldBe([new JobTag("listing_id", "23"), new JobTag("tenant", "acme")], ignoreOrder: true);
    }

    [Fact]
    public async Task JobSummary_ConcurrencyGroupKeys_RoundTrips()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(jobId, groupKeys: ["alpha", "beta"]));

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(ConcurrencyGroupContains: "ALP"));
        var detail = await context.Reader.GetJobAsync(jobId);

        page.Jobs.ShouldHaveSingleItem().ConcurrencyGroupKeys.ShouldBe(["alpha", "beta"]);
        detail.ShouldNotBeNull().Summary.ConcurrencyGroupKeys.ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task JobSummary_ClaimedAtUtc_IsVisibleForClaimedJobs()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(jobId));
        (await ClaimAsync(context.Store)).JobId.ShouldBe(jobId);

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(States: [JobState.Claimed]));
        var detail = await context.Reader.GetJobAsync(jobId);

        page.Jobs.ShouldHaveSingleItem().ClaimedAtUtc.ShouldNotBeNull();
        detail.ShouldNotBeNull().Summary.ClaimedAtUtc.ShouldBe(detail.ClaimedAtUtc);
    }

    [Fact]
    public async Task GetJob_InvocationMetadata_ReconstructsRuntimeBindingsAndJsonArguments()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          jobId,
          serviceType: typeof(InspectionInvocationService).AssemblyQualifiedName,
          methodName: nameof(InspectionInvocationService.RunAsync),
          methodParameterTypes:
          [
              typeof(InspectionPayload).AssemblyQualifiedName!,
              typeof(InspectionDependency).AssemblyQualifiedName!,
              typeof(IJobContext).AssemblyQualifiedName!,
              typeof(CancellationToken).AssemblyQualifiedName!,
          ],
          serializedArguments: new SerializedJobPayload(
            SystemTextJsonJobPayloadSerializer.JsonContentType,
            JsonSerializer.SerializeToUtf8Bytes(new[] { new { name = "alpha", count = 42 } })),
          methodParameterBindings:
          [
              new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
              new JobMethodParameterBinding(JobMethodParameterBindingKind.Service, typeof(InspectionDependency).AssemblyQualifiedName),
              new JobMethodParameterBinding(JobMethodParameterBindingKind.JobContext),
              new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken),
          ]));

        var detail = await context.Reader.GetJobAsync(jobId);

        var invocation = detail.ShouldNotBeNull().Invocation.ShouldNotBeNull();
        invocation.TargetKind.ShouldBe(JobInvocationTargetKind.Instance);
        invocation.ServiceType.ShouldBe(typeof(InspectionInvocationService).AssemblyQualifiedName);
        invocation.MethodName.ShouldBe(nameof(InspectionInvocationService.RunAsync));
        invocation.ReconstructedCall.ShouldBe(string.Join(
          Environment.NewLine,
          "InspectionInvocationService.RunAsync(",
          "    {\"name\":\"alpha\",\"count\":42},",
          "    Job.Resolve<InspectionDependency>(),",
          "    Job.Context,",
          "    CancellationToken)"));
        invocation.SerializedArgumentsContentType.ShouldBe(SystemTextJsonJobPayloadSerializer.JsonContentType);
        invocation.SerializedArgumentsStatus.ShouldBe(JobSerializedArgumentsInspectionStatus.Displayable);
        invocation.Parameters.Select(parameter => parameter.Binding.Kind).ShouldBe([
            JobMethodParameterBindingKind.Serialized,
            JobMethodParameterBindingKind.Service,
            JobMethodParameterBindingKind.JobContext,
            JobMethodParameterBindingKind.CancellationToken,
        ]);
        var valueJson = invocation.Parameters[0].SerializedValueJson.ShouldNotBeNull();
        valueJson.ShouldContain("\"name\": \"alpha\"");
        valueJson.ShouldContain("\"count\": 42");
        invocation.Parameters[1].Binding.ServiceType.ShouldBe(typeof(InspectionDependency).AssemblyQualifiedName);
        invocation.Parameters.Skip(1).All(parameter => parameter.SerializedValueJson is null).ShouldBeTrue();
    }

    [Fact]
    public async Task GetJob_InvocationMetadata_CustomPayloadReportsUnsupportedContentType()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          jobId,
          methodParameterTypes: [typeof(string).AssemblyQualifiedName!],
          serializedArguments: new SerializedJobPayload("application/x-test", [1, 2, 3]),
          methodParameterBindings: [new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized)]));

        var detail = await context.Reader.GetJobAsync(jobId);

        var invocation = detail.ShouldNotBeNull().Invocation.ShouldNotBeNull();
        invocation.SerializedArgumentsContentType.ShouldBe("application/x-test");
        invocation.ReconstructedCall.ShouldContain("<serialized String>");
        invocation.SerializedArgumentsByteCount.ShouldBe(3);
        invocation.SerializedArgumentsStatus.ShouldBe(JobSerializedArgumentsInspectionStatus.UnsupportedContentType);
        invocation.SerializedArgumentsStatusMessage.ShouldNotBeNull().ShouldContain("unsupported content type");
        invocation.Parameters.ShouldHaveSingleItem().SerializedValueJson.ShouldBeNull();
    }

    [Fact]
    public async Task SearchJobs_HandlerSubstringSearch_MatchesAssemblyUnqualifiedHandler()
    {
        await using var context = await this.CreateContextAsync();
        var matchingJob = Guid.NewGuid();
        var otherJob = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(matchingJob));
        await context.Store.EnqueueAsync(CreateRequest(otherJob, methodName: "OtherAsync"));

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          HandlerContains: "InspectionContractService.RunAsync"));

        page.Jobs.Select(job => job.JobId).ShouldBe([matchingJob]);
        page.TotalCount.ShouldBe(1L);
    }

    [Fact]
    public async Task SearchJobs_LiveFilters_ComposeAndMatchSubstringsCaseInsensitive()
    {
        await using var context = await this.CreateContextAsync();
        var queuedMatch = Guid.NewGuid();
        var claimedMatch = Guid.NewGuid();
        var wrongGroup = Guid.NewGuid();
        var wrongState = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          queuedMatch,
          priority: 10,
          groupKeys: ["tenant-acme"],
          tags: [new JobTag("tenant", "acme")],
          serviceType: "Sheddueller.ProviderContracts.Billing.InvoiceHandler",
          methodName: "RunBatch"));
        await context.Store.EnqueueAsync(CreateRequest(
          claimedMatch,
          priority: 100,
          groupKeys: ["tenant-acme-urgent"],
          tags: [new JobTag("tenant", "acme")],
          serviceType: "Sheddueller.ProviderContracts.Billing.InvoiceHandler",
          methodName: "RunBatch"));
        await context.Store.EnqueueAsync(CreateRequest(
          wrongGroup,
          groupKeys: ["tenant-contoso"],
          tags: [new JobTag("tenant", "acme")],
          serviceType: "Sheddueller.ProviderContracts.Billing.InvoiceHandler",
          methodName: "RunBatch"));
        await context.Store.EnqueueAsync(CreateRequest(
          wrongState,
          priority: 50,
          groupKeys: ["tenant-acme"],
          tags: [new JobTag("tenant", "acme")],
          serviceType: "Sheddueller.ProviderContracts.Billing.InvoiceHandler",
          methodName: "RunBatch"));

        (await ClaimAsync(context.Store)).JobId.ShouldBe(claimedMatch);
        var wrongStateClaim = await ClaimAsync(context.Store);
        wrongStateClaim.JobId.ShouldBe(wrongState);
        await context.Store.MarkCompletedAsync(new CompleteJobRequest(
          wrongState,
          "node-1",
          wrongStateClaim.LeaseToken,
          DateTimeOffset.UtcNow));

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          States: [JobState.Queued, JobState.Claimed],
          HandlerContains: "invoicehandler.run",
          TagContains: "TENANT:AC",
          ConcurrencyGroupContains: "ACME"));

        page.Jobs.Select(job => job.JobId).ShouldBe([claimedMatch, queuedMatch]);
        page.TotalCount.ShouldBe(2L);
    }

    [Fact]
    public async Task SearchJobs_PagedQuery_ReturnsTotalMatchingCount()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid(), tags: [new JobTag("tenant", "acme")]));
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid(), tags: [new JobTag("tenant", "acme")]));
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid(), tags: [new JobTag("tenant", "acme")]));
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid(), tags: [new JobTag("tenant", "contoso")]));

        var firstPage = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          TagContains: "tenant:acme",
          PageSize: 2));

        firstPage.Jobs.Count.ShouldBe(2);
        firstPage.TotalCount.ShouldBe(3L);
        firstPage.ContinuationToken.ShouldNotBeNull();

        var secondPage = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          TagContains: "tenant:acme",
          PageSize: 2,
          ContinuationToken: firstPage.ContinuationToken));

        secondPage.Jobs.Count.ShouldBe(1);
        secondPage.TotalCount.ShouldBe(3L);
        secondPage.ContinuationToken.ShouldBeNull();
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
        (await ClaimAsync(context.Store)).JobId.ShouldBe(running);
        await context.Store.EnqueueAsync(CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await context.Store.EnqueueAsync(CreateRequest(delayed, notBeforeUtc: DateTimeOffset.UtcNow.AddMinutes(5)));
        await context.Store.EnqueueAsync(CreateRequest(claimable, priority: 50));

        (await context.Reader.GetQueuePositionAsync(claimable)).Kind.ShouldBe(JobQueuePositionKind.Claimable);
        (await context.Reader.GetQueuePositionAsync(claimable)).Position.ShouldBe(1);
        (await context.Reader.GetQueuePositionAsync(blocked)).Kind.ShouldBe(JobQueuePositionKind.BlockedByConcurrency);
        (await context.Reader.GetQueuePositionAsync(delayed)).Kind.ShouldBe(JobQueuePositionKind.Delayed);
        (await context.Reader.GetQueuePositionAsync(Guid.NewGuid())).Kind.ShouldBe(JobQueuePositionKind.NotFound);
    }

    [Fact]
    public async Task Events_AppendReadLatestProgressAndCleanup_RoundTrips()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(jobId));
        var logEvent = await context.EventSink.AppendAsync(new AppendJobEventRequest(
          jobId,
          JobEventKind.Log,
          AttemptNumber: 0,
          LogLevel: JobLogLevel.Information,
          Message: "starting",
          Fields: new Dictionary<string, string>(StringComparer.Ordinal) { ["step"] = "one" }));
        var progressEvent = await context.EventSink.AppendAsync(new AppendJobEventRequest(
          jobId,
          JobEventKind.Progress,
          AttemptNumber: 0,
          Message: "half",
          ProgressPercent: 50));

        var events = await ReadAllAsync(context.Reader, jobId);
        var sequences = events.Select(jobEvent => jobEvent.EventSequence).ToArray();
        sequences.SequenceEqual(sequences.Order()).ShouldBeTrue();
        logEvent.EventSequence.ShouldBeGreaterThan(0);
        progressEvent.EventSequence.ShouldBeGreaterThan(logEvent.EventSequence);

        var detail = await context.Reader.GetJobAsync(jobId);
        detail.ShouldNotBeNull();
        detail.Summary.LatestProgress.ShouldNotBeNull().Percent.ShouldBe(50);

        var claimed = await ClaimAsync(context.Store);
        await context.Store.MarkCompletedAsync(new CompleteJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        (await context.RetentionStore.CleanupAsync(TimeSpan.FromMilliseconds(1))).ShouldBeGreaterThan(0);
        (await ReadAllAsync(context.Reader, jobId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ScheduleViews_TagsAndPauseState_RoundTrip()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule(
          "hourly-cleanup",
          tags: [new JobTag("area", "billing")]));

        var page = await context.ScheduleReader.SearchSchedulesAsync(new ScheduleInspectionQuery(Tag: new JobTag("area", "billing")));
        page.Schedules.ShouldHaveSingleItem().ScheduleKey.ShouldBe("hourly-cleanup");
        page.TotalCount.ShouldBe(1L);

        var scheduleKeyPage = await context.ScheduleReader.SearchSchedulesAsync(new ScheduleInspectionQuery(ScheduleKey: "CLEAN"));
        scheduleKeyPage.Schedules.ShouldHaveSingleItem().ScheduleKey.ShouldBe("hourly-cleanup");
        scheduleKeyPage.TotalCount.ShouldBe(1L);

        (await context.Store.PauseRecurringScheduleAsync("hourly-cleanup", DateTimeOffset.UtcNow)).ShouldBeTrue();

        var detail = await context.ScheduleReader.GetScheduleAsync("hourly-cleanup");
        detail.ShouldNotBeNull();
        detail.Summary.IsPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrencyGroupView_SaturatedGroup_ShowsClaimedAndBlockedJobs()
    {
        await using var context = await this.CreateContextAsync();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(running, groupKeys: ["shared"]));
        (await ClaimAsync(context.Store)).JobId.ShouldBe(running);
        await context.Store.EnqueueAsync(CreateRequest(blocked, groupKeys: ["shared"]));
        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("idle", 2, DateTimeOffset.UtcNow));

        var page = await context.ConcurrencyGroupReader.SearchConcurrencyGroupsAsync(new ConcurrencyGroupInspectionQuery(GroupKey: "shared"));
        var saturatedPage = await context.ConcurrencyGroupReader.SearchConcurrencyGroupsAsync(
          new ConcurrencyGroupInspectionQuery(IsSaturated: true, HasBlockedJobs: true));
        var detail = await context.ConcurrencyGroupReader.GetConcurrencyGroupAsync("shared");

        page.Groups.ShouldHaveSingleItem().GroupKey.ShouldBe("shared");
        page.TotalCount.ShouldBe(1L);
        saturatedPage.Groups.ShouldHaveSingleItem().GroupKey.ShouldBe("shared");
        saturatedPage.TotalCount.ShouldBe(1L);
        detail.ShouldNotBeNull();
        detail.Summary.EffectiveLimit.ShouldBe(1);
        detail.Summary.CurrentOccupancy.ShouldBe(1);
        detail.Summary.IsSaturated.ShouldBeTrue();
        detail.ClaimedJobIds.ShouldBe([running]);
        detail.BlockedJobIds.ShouldBe([blocked]);
    }

    [Fact]
    public async Task ConcurrencyGroupSearch_GroupKeyFilter_IsExactAndReturnsTotalCount()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("shared", 2, DateTimeOffset.UtcNow));
        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("shared-extra", 2, DateTimeOffset.UtcNow));

        var page = await context.ConcurrencyGroupReader.SearchConcurrencyGroupsAsync(
          new ConcurrencyGroupInspectionQuery(GroupKey: "shared"));

        page.Groups.ShouldHaveSingleItem().GroupKey.ShouldBe("shared");
        page.TotalCount.ShouldBe(1L);
        page.ContinuationToken.ShouldBeNull();
    }

    [Fact]
    public async Task ConcurrencyGroupSearch_PagedQuery_ReturnsTotalCountBeforeContinuation()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("group-a", 2, DateTimeOffset.UtcNow));
        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("group-b", 2, DateTimeOffset.UtcNow));
        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("group-c", 2, DateTimeOffset.UtcNow));

        var firstPage = await context.ConcurrencyGroupReader.SearchConcurrencyGroupsAsync(
          new ConcurrencyGroupInspectionQuery(PageSize: 1));

        firstPage.Groups.Select(group => group.GroupKey).ShouldBe(["group-a"]);
        firstPage.TotalCount.ShouldBe(3L);
        firstPage.ContinuationToken.ShouldNotBeNull();

        var secondPage = await context.ConcurrencyGroupReader.SearchConcurrencyGroupsAsync(
          new ConcurrencyGroupInspectionQuery(PageSize: 1, ContinuationToken: firstPage.ContinuationToken));

        secondPage.Groups.Select(group => group.GroupKey).ShouldBe(["group-b"]);
        secondPage.TotalCount.ShouldBe(3L);
        secondPage.ContinuationToken.ShouldNotBeNull();
    }

    [Fact]
    public async Task NodeAndMetrics_ReadsHeartbeatAndRollingCounts()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-v5",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 1));
        await context.Store.EnqueueAsync(CreateRequest(jobId));

        var nodes = await context.NodeReader.SearchNodesAsync(new NodeInspectionQuery());
        var nodeDetail = await context.NodeReader.GetNodeAsync("node-v5");
        var metrics = await context.MetricsReader.GetMetricsAsync(new MetricsInspectionQuery([TimeSpan.FromMinutes(5)]));

        nodes.TotalCount.ShouldBeGreaterThanOrEqualTo(1L);
        nodes.Nodes.ShouldContain(node => node.NodeId == "node-v5"
          && node.State == NodeHealthState.Active
          && node.MaxConcurrentExecutionsPerNode == 4
          && node.CurrentExecutionCount == 1);
        nodeDetail.ShouldNotBeNull().Summary.NodeId.ShouldBe("node-v5");
        metrics.Windows.ShouldHaveSingleItem().QueuedCount.ShouldBeGreaterThanOrEqualTo(1);
        metrics.Windows[0].ActiveNodeCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task NodeSearch_StateFilter_ReturnsTotalMatchingCount()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-a",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 1));
        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-b",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 0));

        var page = await context.NodeReader.SearchNodesAsync(
          new NodeInspectionQuery(State: NodeHealthState.Active, PageSize: 1));

        page.Nodes.Count.ShouldBe(1);
        page.Nodes[0].State.ShouldBe(NodeHealthState.Active);
        page.TotalCount.ShouldBe(2L);
        page.ContinuationToken.ShouldNotBeNull();
    }

    [Fact]
    public async Task NodeSearch_PagedQuery_ReturnsTotalCountBeforeContinuation()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-a",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 1));
        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-b",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 0));
        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-c",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 0));

        var firstPage = await context.NodeReader.SearchNodesAsync(new NodeInspectionQuery(PageSize: 1));

        firstPage.Nodes.Select(node => node.NodeId).ShouldBe(["node-a"]);
        firstPage.TotalCount.ShouldBe(3L);
        firstPage.ContinuationToken.ShouldNotBeNull();

        var secondPage = await context.NodeReader.SearchNodesAsync(
          new NodeInspectionQuery(PageSize: 1, ContinuationToken: firstPage.ContinuationToken));

        secondPage.Nodes.Select(node => node.NodeId).ShouldBe(["node-b"]);
        secondPage.TotalCount.ShouldBe(3L);
        secondPage.ContinuationToken.ShouldNotBeNull();
    }

    protected static async ValueTask<ClaimedJob> ClaimAsync(IJobStore store)
      => (await store.TryClaimNextAsync(new ClaimJobRequest("node-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30))))
        .ShouldBeOfType<ClaimJobResult.Claimed>()
        .Job;

    protected static EnqueueJobRequest CreateRequest(
        Guid jobId,
        int priority = 0,
        DateTimeOffset? notBeforeUtc = null,
        IReadOnlyList<string>? groupKeys = null,
        IReadOnlyList<JobTag>? tags = null,
        int maxAttempts = 1,
        RetryBackoffKind? retryBackoffKind = null,
        TimeSpan? retryBaseDelay = null,
        string? serviceType = null,
        string? methodName = null,
        IReadOnlyList<string>? methodParameterTypes = null,
        SerializedJobPayload? serializedArguments = null,
        JobInvocationTargetKind invocationTargetKind = JobInvocationTargetKind.Instance,
        IReadOnlyList<JobMethodParameterBinding>? methodParameterBindings = null)
      => new(
        jobId,
        priority,
        serviceType ?? typeof(InspectionContractService).AssemblyQualifiedName!,
        methodName ?? nameof(InspectionContractService.RunAsync),
        methodParameterTypes ?? [typeof(CancellationToken).AssemblyQualifiedName!],
        serializedArguments ?? new SerializedJobPayload(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
        groupKeys ?? [],
        DateTimeOffset.UtcNow,
        notBeforeUtc,
        maxAttempts,
        retryBackoffKind,
        retryBaseDelay,
        Tags: tags,
        InvocationTargetKind: invocationTargetKind,
        MethodParameterBindings: methodParameterBindings);

    protected static UpsertRecurringScheduleRequest CreateSchedule(
        string scheduleKey,
        IReadOnlyList<JobTag>? tags = null)
      => new(
        scheduleKey,
        "* * * * *",
        typeof(InspectionContractService).AssemblyQualifiedName!,
        nameof(InspectionContractService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        new SerializedJobPayload(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
        Priority: 0,
        ConcurrencyGroupKeys: [],
        RetryPolicy: null,
        RecurringOverlapMode.Skip,
        DateTimeOffset.UtcNow,
        tags);

    private static async ValueTask<IReadOnlyList<JobEvent>> ReadAllAsync(IJobInspectionReader reader, Guid jobId)
    {
        var events = new List<JobEvent>();
        await foreach (var jobEvent in reader.ReadEventsAsync(jobId))
        {
            events.Add(jobEvent);
        }

        return events;
    }

    private sealed class InspectionContractService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }

    private sealed record InspectionPayload(string Name, int Count);

    private sealed class InspectionDependency
    {
    }

    private sealed class InspectionInvocationService
    {
        public Task RunAsync(
            InspectionPayload payload,
            InspectionDependency dependency,
            IJobContext context,
            CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
