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
        page.Jobs[0].Tags.ShouldBe([new JobTag("listing_id", "23"), new JobTag("tenant", "acme")]);
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
    public async Task SearchJobs_DefaultSort_OrdersClaimedThenQueuedByClaimOrder()
    {
        await using var context = await this.CreateContextAsync();
        var firstLow = Guid.NewGuid();
        var secondLow = Guid.NewGuid();
        var high = Guid.NewGuid();
        var running = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(firstLow, priority: 0));
        await context.Store.EnqueueAsync(CreateRequest(secondLow, priority: 0));
        await context.Store.EnqueueAsync(CreateRequest(high, priority: 10));
        await context.Store.EnqueueAsync(CreateRequest(running, priority: 100));
        (await ClaimAsync(context.Store)).JobId.ShouldBe(running);

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          States: [JobState.Queued, JobState.Claimed]));

        page.Jobs.Select(job => job.JobId).ShouldBe([running, high, firstLow, secondLow]);
        page.Jobs.Select(job => job.QueuePosition?.Kind).ShouldBe([
            JobQueuePositionKind.Claimed,
            JobQueuePositionKind.Claimable,
            JobQueuePositionKind.Claimable,
            JobQueuePositionKind.Claimable,
        ]);
        page.Jobs.Select(job => job.QueuePosition?.Position).ShouldBe([null, 1L, 2L, 3L]);
    }

    [Fact]
    public async Task SearchJobs_PagedQueuedResults_KeepGlobalQueuePositions()
    {
        await using var context = await this.CreateContextAsync();
        var jobIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        foreach (var jobId in jobIds)
        {
            await context.Store.EnqueueAsync(CreateRequest(jobId));
        }

        var firstPage = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          States: [JobState.Queued],
          PageSize: 2));
        var secondPage = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          States: [JobState.Queued],
          PageSize: 2,
          ContinuationToken: firstPage.ContinuationToken));

        firstPage.Jobs.Select(job => job.JobId).ShouldBe([jobIds[0], jobIds[1]]);
        firstPage.Jobs.Select(job => job.QueuePosition?.Position).ShouldBe([1L, 2L]);
        secondPage.Jobs.Select(job => job.JobId).ShouldBe([jobIds[2], jobIds[3]]);
        secondPage.Jobs.Select(job => job.QueuePosition?.Position).ShouldBe([3L, 4L]);
    }

    [Fact]
    public async Task SearchJobs_MixedStates_ReportsEquivalentQueuePositionKinds()
    {
        await using var context = await this.CreateContextAsync();
        var completed = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var retryWaiting = Guid.NewGuid();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var claimable = Guid.NewGuid();
        var delayed = Guid.NewGuid();
        var canceled = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(completed));
        var completedClaim = await ClaimAsync(context.Store);
        completedClaim.JobId.ShouldBe(completed);
        (await context.Store.MarkCompletedAsync(new CompleteJobRequest(completed, "node-1", completedClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();

        await context.Store.EnqueueAsync(CreateRequest(failed));
        var failedClaim = await ClaimAsync(context.Store);
        failedClaim.JobId.ShouldBe(failed);
        (await context.Store.MarkFailedAsync(new FailJobRequest(failed, "node-1", failedClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();

        await context.Store.EnqueueAsync(CreateRequest(
          retryWaiting,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromHours(1)));
        var retryClaim = await ClaimAsync(context.Store);
        retryClaim.JobId.ShouldBe(retryWaiting);
        (await context.Store.MarkFailedAsync(new FailJobRequest(retryWaiting, "node-1", retryClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();

        await context.Store.EnqueueAsync(CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await ClaimAsync(context.Store)).JobId.ShouldBe(running);
        await context.Store.EnqueueAsync(CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await context.Store.EnqueueAsync(CreateRequest(claimable, priority: 50));
        await context.Store.EnqueueAsync(CreateRequest(delayed, notBeforeUtc: DateTimeOffset.UtcNow.AddHours(1)));
        await context.Store.EnqueueAsync(CreateRequest(canceled));
        (await context.Store.CancelAsync(new CancelJobRequest(canceled, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.Canceled);

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(PageSize: 20));
        var jobsById = page.Jobs.ToDictionary(static job => job.JobId);

        jobsById[running].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Claimed);
        jobsById[claimable].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Claimable);
        jobsById[claimable].QueuePosition?.Position.ShouldBe(1);
        jobsById[blocked].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.BlockedByConcurrency);
        jobsById[retryWaiting].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.RetryWaiting);
        jobsById[delayed].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Delayed);
        jobsById[completed].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Terminal);
        jobsById[failed].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Terminal);
        jobsById[canceled].QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Canceled);
    }

    [Fact]
    public async Task Overview_MixedStates_ReturnsIndependentSectionsWithHydratedSummaries()
    {
        await using var context = await this.CreateContextAsync();
        var failed = Guid.NewGuid();
        var retryWaiting = Guid.NewGuid();
        var running = Guid.NewGuid();
        var claimable = Guid.NewGuid();
        var delayed = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          failed,
          groupKeys: ["overview-group"],
          tags: [new JobTag("tenant", "acme")]));
        await context.EventSink.AppendAsync(new AppendJobEventRequest(
          failed,
          JobEventKind.Progress,
          AttemptNumber: 0,
          Message: "halfway",
          ProgressPercent: 50));
        var failedClaim = await ClaimAsync(context.Store);
        failedClaim.JobId.ShouldBe(failed);
        (await context.Store.MarkFailedAsync(new FailJobRequest(failed, "node-1", failedClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();

        await context.Store.EnqueueAsync(CreateRequest(
          retryWaiting,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromHours(1)));
        var retryClaim = await ClaimAsync(context.Store);
        retryClaim.JobId.ShouldBe(retryWaiting);
        (await context.Store.MarkFailedAsync(new FailJobRequest(retryWaiting, "node-1", retryClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();

        await context.Store.EnqueueAsync(CreateRequest(running));
        (await ClaimAsync(context.Store)).JobId.ShouldBe(running);
        await context.Store.EnqueueAsync(CreateRequest(claimable, priority: 10));
        await context.Store.EnqueueAsync(CreateRequest(delayed, notBeforeUtc: DateTimeOffset.UtcNow.AddHours(1)));

        var overview = await context.Reader.GetOverviewAsync();

        var runningSummary = overview.RunningJobs.Single(job => job.JobId == running);
        runningSummary.QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Claimed);

        var failedSummary = overview.RecentlyFailedJobs.Single(job => job.JobId == failed);
        failedSummary.Tags.ShouldBe([new JobTag("tenant", "acme")]);
        failedSummary.ConcurrencyGroupKeys.ShouldBe(["overview-group"]);
        failedSummary.LatestProgress.ShouldNotBeNull().Message.ShouldBe("halfway");
        failedSummary.QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Terminal);

        var claimableSummary = overview.QueuedJobs.Single(job => job.JobId == claimable);
        claimableSummary.QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Claimable);
        claimableSummary.QueuePosition?.Position.ShouldBe(1);
        overview.DelayedJobs.Single(job => job.JobId == delayed).QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Delayed);
        overview.RetryWaitingJobs.Single(job => job.JobId == retryWaiting).QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.RetryWaiting);
    }

    [Fact]
    public async Task Overview_DelayedBacklog_DoesNotHideClaimableJobs()
    {
        await using var context = await this.CreateContextAsync();
        var claimable = Guid.NewGuid();
        var delayedRequests = Enumerable
          .Range(0, 105)
          .Select(index => CreateRequest(Guid.NewGuid(), notBeforeUtc: DateTimeOffset.UtcNow.AddHours(1).AddMinutes(index)))
          .ToArray();

        await context.Store.EnqueueManyAsync(delayedRequests);
        await context.Store.EnqueueAsync(CreateRequest(claimable, priority: 100));

        var overview = await context.Reader.GetOverviewAsync();

        overview.QueuedJobs.Select(job => job.JobId).ShouldContain(claimable);
        overview.QueuedJobs.Single(job => job.JobId == claimable).QueuePosition?.Kind.ShouldBe(JobQueuePositionKind.Claimable);
        overview.DelayedJobs.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Overview_ClaimableBacklog_DoesNotHideWaitingSections()
    {
        await using var context = await this.CreateContextAsync();
        var retryWaiting = Guid.NewGuid();
        var delayed = Guid.NewGuid();
        var claimableRequests = Enumerable
          .Range(0, 105)
          .Select(index => CreateRequest(Guid.NewGuid(), priority: 100 - index))
          .ToArray();

        await context.Store.EnqueueAsync(CreateRequest(
          retryWaiting,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromHours(1)));
        var retryClaim = await ClaimAsync(context.Store);
        retryClaim.JobId.ShouldBe(retryWaiting);
        (await context.Store.MarkFailedAsync(new FailJobRequest(retryWaiting, "node-1", retryClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();
        await context.Store.EnqueueAsync(CreateRequest(delayed, notBeforeUtc: DateTimeOffset.UtcNow.AddHours(1)));
        await context.Store.EnqueueManyAsync(claimableRequests);

        var overview = await context.Reader.GetOverviewAsync();

        overview.QueuedJobs.Count.ShouldBe(10);
        overview.QueuedJobs.All(job => job.QueuePosition?.Kind == JobQueuePositionKind.Claimable).ShouldBeTrue();
        overview.DelayedJobs.Select(job => job.JobId).ShouldContain(delayed);
        overview.RetryWaitingJobs.Select(job => job.JobId).ShouldContain(retryWaiting);
    }

    [Fact]
    public async Task SearchJobs_NewestFirstSort_OrdersByNewestEnqueueSequence()
    {
        await using var context = await this.CreateContextAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(first));
        await context.Store.EnqueueAsync(CreateRequest(second));
        await context.Store.EnqueueAsync(CreateRequest(third));

        var page = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          Sort: JobInspectionSort.NewestFirst));

        page.Jobs.Select(job => job.JobId).ShouldBe([third, second, first]);
    }

    [Fact]
    public async Task SearchJobs_PagedQuery_ReturnsTotalMatchingCount()
    {
        await using var context = await this.CreateContextAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(first, tags: [new JobTag("tenant", "acme")]));
        await context.Store.EnqueueAsync(CreateRequest(second, tags: [new JobTag("tenant", "acme")]));
        await context.Store.EnqueueAsync(CreateRequest(third, tags: [new JobTag("tenant", "acme")]));
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid(), tags: [new JobTag("tenant", "contoso")]));

        var firstPage = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          TagContains: "tenant:acme",
          PageSize: 2));

        firstPage.Jobs.Select(job => job.JobId).ShouldBe([first, second]);
        firstPage.Jobs.Count.ShouldBe(2);
        firstPage.TotalCount.ShouldBe(3L);
        firstPage.ContinuationToken.ShouldNotBeNull();

        var secondPage = await context.Reader.SearchJobsAsync(new JobInspectionQuery(
          TagContains: "tenant:acme",
          PageSize: 2,
          ContinuationToken: firstPage.ContinuationToken));

        secondPage.Jobs.Select(job => job.JobId).ShouldBe([third]);
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
          tags: [new JobTag("tenant", "acme"), new JobTag("area", "billing")]));

        var page = await context.ScheduleReader.SearchSchedulesAsync(new ScheduleInspectionQuery(Tag: new JobTag("area", "billing")));
        page.Schedules.ShouldHaveSingleItem().ScheduleKey.ShouldBe("hourly-cleanup");
        page.Schedules[0].Tags.ShouldBe([new JobTag("tenant", "acme"), new JobTag("area", "billing")]);
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
    public async Task Metrics_RollingActivity_ReturnsRolledUpRatesAndPercentiles()
    {
        await using var context = await this.CreateContextAsync();
        var completed = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var canceled = Guid.NewGuid();
        var scheduled = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(completed));
        var completedClaim = await ClaimAsync(context.Store);
        await context.Store.MarkCompletedAsync(new CompleteJobRequest(
          completed,
          "node-1",
          completedClaim.LeaseToken,
          DateTimeOffset.UtcNow));

        await context.Store.EnqueueAsync(CreateRequest(failed));
        var failedClaim = await ClaimAsync(context.Store);
        await context.Store.MarkFailedAsync(new FailJobRequest(
          failed,
          "node-1",
          failedClaim.LeaseToken,
          DateTimeOffset.UtcNow,
          CreateFailure()));

        await context.Store.EnqueueAsync(CreateRequest(canceled));
        (await context.Store.CancelAsync(new CancelJobRequest(canceled, DateTimeOffset.UtcNow)))
          .ShouldBe(JobCancellationResult.Canceled);

        await context.Store.EnqueueAsync(CreateRequest(
          scheduled,
          sourceScheduleKey: "nightly",
          scheduledFireAtUtc: DateTimeOffset.UtcNow.AddSeconds(-2),
          scheduleOccurrenceKind: ScheduleOccurrenceKind.Automatic));

        var metrics = await context.MetricsReader.GetMetricsAsync(new MetricsInspectionQuery([TimeSpan.FromMinutes(5)]));
        var window = metrics.Windows.ShouldHaveSingleItem();

        window.EnqueueRatePerMinute.ShouldBeGreaterThan(0);
        window.ClaimRatePerMinute.ShouldBeGreaterThan(0);
        window.SuccessRatePerMinute.ShouldBeGreaterThan(0);
        window.FailureRatePerMinute.ShouldBeGreaterThan(0);
        window.CancellationRatePerMinute.ShouldBeGreaterThan(0);
        window.RetryRatePerMinute.ShouldBeGreaterThan(0);
        window.P50QueueLatency.ShouldNotBeNull();
        window.P95ExecutionDuration.ShouldNotBeNull();
        window.P95ScheduleFireLag.ShouldNotBeNull();
    }

    [Fact]
    public async Task Metrics_BulkQueuedCancellation_ReturnsRolledUpCancellationRate()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid()));
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid()));

        (await context.Store.CancelQueuedJobsAsync(new CancelQueuedJobsRequest(DateTimeOffset.UtcNow))).ShouldBe(2);

        var metrics = await context.MetricsReader.GetMetricsAsync(new MetricsInspectionQuery([TimeSpan.FromMinutes(5)]));

        metrics.Windows.ShouldHaveSingleItem().CancellationRatePerMinute.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task NodeSearch_MultipleClaimedJobsAcrossNodes_ReturnsPerNodeClaimedCounts()
    {
        await using var context = await this.CreateContextAsync();
        var nodeAFirst = Guid.NewGuid();
        var nodeASecond = Guid.NewGuid();
        var nodeBJob = Guid.NewGuid();

        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-a",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 2));
        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-b",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 1));
        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-c",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 0));
        await context.Store.EnqueueAsync(CreateRequest(nodeAFirst));
        await context.Store.EnqueueAsync(CreateRequest(nodeASecond));
        await context.Store.EnqueueAsync(CreateRequest(nodeBJob));

        (await ClaimAsync(context.Store, "node-a")).JobId.ShouldBe(nodeAFirst);
        (await ClaimAsync(context.Store, "node-a")).JobId.ShouldBe(nodeASecond);
        (await ClaimAsync(context.Store, "node-b")).JobId.ShouldBe(nodeBJob);

        var page = await context.NodeReader.SearchNodesAsync(new NodeInspectionQuery(PageSize: 10));
        var nodesById = page.Nodes.ToDictionary(node => node.NodeId);

        nodesById["node-a"].ClaimedJobCount.ShouldBe(2);
        nodesById["node-b"].ClaimedJobCount.ShouldBe(1);
        nodesById["node-c"].ClaimedJobCount.ShouldBe(0);
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
        page.Nodes.Select(node => node.NodeId).ShouldBe(["node-a"]);
        page.Nodes[0].State.ShouldBe(NodeHealthState.Active);
        page.TotalCount.ShouldBe(2L);
        page.ContinuationToken.ShouldNotBeNull();

        var secondPage = await context.NodeReader.SearchNodesAsync(
          new NodeInspectionQuery(State: NodeHealthState.Active, PageSize: 1, ContinuationToken: page.ContinuationToken));

        secondPage.Nodes.Select(node => node.NodeId).ShouldBe(["node-b"]);
        secondPage.TotalCount.ShouldBe(2L);
        secondPage.ContinuationToken.ShouldBeNull();
    }

    [Fact]
    public async Task NodeDetail_ClaimedJobs_ReturnsClaimedJobIdsForNode()
    {
        await using var context = await this.CreateContextAsync();
        var nodeAFirst = Guid.NewGuid();
        var nodeBJob = Guid.NewGuid();
        var nodeASecond = Guid.NewGuid();

        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-a",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 2));
        await context.Store.RecordWorkerNodeHeartbeatAsync(new WorkerNodeHeartbeatRequest(
          "node-b",
          DateTimeOffset.UtcNow,
          MaxConcurrentExecutionsPerNode: 4,
          CurrentExecutionCount: 1));
        await context.Store.EnqueueAsync(CreateRequest(nodeAFirst));
        await context.Store.EnqueueAsync(CreateRequest(nodeBJob));
        await context.Store.EnqueueAsync(CreateRequest(nodeASecond));

        (await ClaimAsync(context.Store, "node-a")).JobId.ShouldBe(nodeAFirst);
        (await ClaimAsync(context.Store, "node-b")).JobId.ShouldBe(nodeBJob);
        (await ClaimAsync(context.Store, "node-a")).JobId.ShouldBe(nodeASecond);

        var detail = await context.NodeReader.GetNodeAsync("node-a");

        detail.ShouldNotBeNull().Summary.ClaimedJobCount.ShouldBe(2);
        detail.ClaimedJobIds.ShouldBe([nodeAFirst, nodeASecond]);
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

    protected static async ValueTask<ClaimedJob> ClaimAsync(
        IJobStore store,
        string nodeId = "node-1")
      => (await store.TryClaimNextAsync(new ClaimJobRequest(nodeId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30))))
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
        IReadOnlyList<JobMethodParameterBinding>? methodParameterBindings = null,
        string? sourceScheduleKey = null,
        DateTimeOffset? scheduledFireAtUtc = null,
        ScheduleOccurrenceKind? scheduleOccurrenceKind = null)
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
        SourceScheduleKey: sourceScheduleKey,
        ScheduledFireAtUtc: scheduledFireAtUtc,
        Tags: tags,
        ScheduleOccurrenceKind: scheduleOccurrenceKind,
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

    private static JobFailureInfo CreateFailure()
      => new("TestException", "failed", "stack");

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
