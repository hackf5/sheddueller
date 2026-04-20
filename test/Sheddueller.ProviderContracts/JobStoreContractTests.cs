namespace Sheddueller.ProviderContracts;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public abstract class JobStoreContractTests
{
    protected static readonly DateTimeOffset ContractClock = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    protected abstract ValueTask<JobStoreContractContext> CreateContextAsync();

    [Fact]
    public async Task EnqueuedJob_Claim_RoundTripsSubmittedMetadata()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();
        var request = CreateRequest(
          jobId,
          priority: 7,
          maxAttempts: 3,
          retryBackoffKind: RetryBackoffKind.Exponential,
          retryBaseDelay: TimeSpan.FromMilliseconds(50),
          retryMaxDelay: TimeSpan.FromMilliseconds(75),
          groupKeys: ["alpha", "beta"]);

        var enqueueResult = await context.Store.EnqueueAsync(request);
        var claimed = await ClaimAsync(context.Store);

        enqueueResult.JobId.ShouldBe(jobId);
        enqueueResult.EnqueueSequence.ShouldBeGreaterThan(0);
        claimed.JobId.ShouldBe(jobId);
        claimed.Priority.ShouldBe(7);
        claimed.ServiceType.ShouldBe(request.ServiceType);
        claimed.MethodName.ShouldBe(request.MethodName);
        claimed.MethodParameterTypes.ShouldBe(request.MethodParameterTypes);
        claimed.SerializedArguments.ContentType.ShouldBe(request.SerializedArguments.ContentType);
        claimed.SerializedArguments.Data.ShouldBe(request.SerializedArguments.Data);
        claimed.ConcurrencyGroupKeys.ShouldBe(["alpha", "beta"]);
        claimed.AttemptCount.ShouldBe(1);
        claimed.MaxAttempts.ShouldBe(3);
        claimed.RetryBackoffKind.ShouldBe(RetryBackoffKind.Exponential);
        claimed.RetryBaseDelay.ShouldBe(TimeSpan.FromMilliseconds(50));
        claimed.RetryMaxDelay.ShouldBe(TimeSpan.FromMilliseconds(75));
    }

    [Fact]
    public async Task TryClaimNext_PriorityAndFifo_ClaimsHigherPriorityThenOldest()
    {
        await using var context = await this.CreateContextAsync();
        var firstLow = Guid.NewGuid();
        var secondLow = Guid.NewGuid();
        var high = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(firstLow, priority: 0));
        await context.Store.EnqueueAsync(CreateRequest(secondLow, priority: 0));
        await context.Store.EnqueueAsync(CreateRequest(high, priority: 10));

        (await ClaimAsync(context.Store)).JobId.ShouldBe(high);
        (await ClaimAsync(context.Store)).JobId.ShouldBe(firstLow);
        (await ClaimAsync(context.Store)).JobId.ShouldBe(secondLow);
    }

    [Fact]
    public async Task DelayedJob_FutureNotBefore_IsNotClaimableUntilDue()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();
        var dueAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(250);

        await context.Store.EnqueueAsync(CreateRequest(jobId, notBeforeUtc: dueAtUtc));

        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        (await ClaimAsync(context.Store)).JobId.ShouldBe(jobId);
    }

    [Fact]
    public async Task TryClaimNext_ConcurrentAttempts_ClaimsJobOnlyOnce()
    {
        await using var context = await this.CreateContextAsync();
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid()));

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
          .Select(index => context.Store.TryClaimNextAsync(CreateClaimRequest($"node-{index}")).AsTask()));

        results.Count(result => result is ClaimJobResult.Claimed).ShouldBe(1);
        results.Count(result => result is ClaimJobResult.NoJobAvailable).ShouldBe(9);
    }

    [Fact]
    public async Task ConcurrencyGroups_SaturatedGroup_SkipsOnlyBlockedJobs()
    {
        await using var context = await this.CreateContextAsync();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await ClaimAsync(context.Store)).JobId.ShouldBe(running);

        await context.Store.EnqueueAsync(CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await context.Store.EnqueueAsync(CreateRequest(eligible, priority: 0));

        (await ClaimAsync(context.Store)).JobId.ShouldBe(eligible);
    }

    [Fact]
    public async Task ConcurrencyLimit_LoweredBelowOccupancy_BlocksFutureClaimsWithoutPreemptingRunningWork()
    {
        await using var context = await this.CreateContextAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var waiting = Guid.NewGuid();

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 2, ContractClock));
        await context.Store.EnqueueAsync(CreateRequest(first, groupKeys: ["critical"]));
        await context.Store.EnqueueAsync(CreateRequest(second, groupKeys: ["critical"]));
        await context.Store.EnqueueAsync(CreateRequest(waiting, groupKeys: ["critical"]));

        var firstClaim = await ClaimAsync(context.Store);
        firstClaim.JobId.ShouldBe(first);
        var secondClaim = await ClaimAsync(context.Store);
        secondClaim.JobId.ShouldBe(second);

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 1, ContractClock));
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        (await context.Store.MarkCompletedAsync(new CompleteJobRequest(first, "node-1", firstClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        (await context.Store.MarkCompletedAsync(new CompleteJobRequest(second, "node-1", secondClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();
        (await ClaimAsync(context.Store)).JobId.ShouldBe(waiting);
    }

    [Fact]
    public async Task MarkCompleted_CurrentLease_RemovesJobFromClaimableWork()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(jobId));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.MarkCompletedAsync(new CompleteJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
    }

    [Fact]
    public async Task MarkFailed_NoRetryPolicy_FailsTerminally()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(jobId));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
    }

    [Fact]
    public async Task MarkFailed_FixedRetryPolicy_RequeuesAfterBackoff()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          jobId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(250)));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        var retry = await ClaimAsync(context.Store);
        retry.JobId.ShouldBe(jobId);
        retry.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task LeaseOwner_StaleToken_CannotMutateReclaimedJob()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          jobId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(1)));

        var staleClaim = await ClaimAsync(context.Store, "node-1", TimeSpan.FromMilliseconds(150));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        (await context.Store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(DateTimeOffset.UtcNow))).ShouldBe(1);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        var currentClaim = await ClaimAsync(context.Store, "node-2");

        (await context.Store.MarkCompletedAsync(new CompleteJobRequest(jobId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeFalse();
        (await context.Store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeFalse();
        (await context.Store.RenewLeaseAsync(new RenewLeaseRequest(jobId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30)))).ShouldBeFalse();
        (await context.Store.ReleaseJobAsync(new ReleaseJobRequest(jobId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeFalse();
        currentClaim.LeaseToken.ShouldNotBe(staleClaim.LeaseToken);
    }

    [Fact]
    public async Task ReleaseJob_CurrentLease_RequeuesWithoutRetryBudgetConsumption()
    {
        await using var context = await this.CreateContextAsync();
        var jobId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(jobId));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.ReleaseJobAsync(new ReleaseJobRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var reclaimed = await ClaimAsync(context.Store);
        reclaimed.JobId.ShouldBe(jobId);
        reclaimed.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_QueuedJobOnly_CancelsBeforeClaim()
    {
        await using var context = await this.CreateContextAsync();
        var queued = Guid.NewGuid();
        var claimed = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(queued));
        await context.Store.EnqueueAsync(CreateRequest(claimed));

        (await context.Store.CancelAsync(new CancelJobRequest(queued, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var claimedJob = await ClaimAsync(context.Store);
        (await context.Store.CancelAsync(new CancelJobRequest(claimedJob.JobId, DateTimeOffset.UtcNow))).ShouldBeFalse();
        claimedJob.JobId.ShouldBe(claimed);
    }

    [Fact]
    public async Task ConcurrencyLimit_SetAndGet_RoundTripsConfiguredLimit()
    {
        await using var context = await this.CreateContextAsync();

        (await context.Store.GetConfiguredConcurrencyLimitAsync("shared")).ShouldBeNull();

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("shared", 3, ContractClock));
        (await context.Store.GetConfiguredConcurrencyLimitAsync("shared")).ShouldBe(3);

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("shared", 5, ContractClock));
        (await context.Store.GetConfiguredConcurrencyLimitAsync("shared")).ShouldBe(5);
    }

    [Fact]
    public async Task RecurringSchedule_Lifecycle_CreateUpdatePauseResumeListDelete()
    {
        await using var context = await this.CreateContextAsync();

        (await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-b", priority: 0))).ShouldBe(RecurringScheduleUpsertResult.Created);
        (await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 0))).ShouldBe(RecurringScheduleUpsertResult.Created);
        (await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 0))).ShouldBe(RecurringScheduleUpsertResult.Unchanged);
        (await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 5, groupKeys: ["group-a"]))).ShouldBe(RecurringScheduleUpsertResult.Updated);

        var schedule = await context.Store.GetRecurringScheduleAsync("schedule-a");
        schedule.ShouldNotBeNull();
        schedule.Priority.ShouldBe(5);
        schedule.ConcurrencyGroupKeys.ShouldBe(["group-a"]);

        var listed = await context.Store.ListRecurringSchedulesAsync();
        listed.Select(item => item.ScheduleKey).ShouldBe(["schedule-a", "schedule-b"]);

        (await context.Store.PauseRecurringScheduleAsync("schedule-a", ContractClock)).ShouldBeTrue();
        (await context.Store.GetRecurringScheduleAsync("schedule-a")).ShouldNotBeNull().IsPaused.ShouldBeTrue();
        (await context.Store.ResumeRecurringScheduleAsync("schedule-a", DateTimeOffset.UtcNow)).ShouldBeTrue();
        (await context.Store.GetRecurringScheduleAsync("schedule-a")).ShouldNotBeNull().IsPaused.ShouldBeFalse();

        (await context.Store.DeleteRecurringScheduleAsync("schedule-a")).ShouldBeTrue();
        (await context.Store.GetRecurringScheduleAsync("schedule-a")).ShouldBeNull();
        (await context.Store.DeleteRecurringScheduleAsync("schedule-a")).ShouldBeFalse();
    }

    [Fact]
    public async Task RecurringSchedule_DueOccurrence_MaterializesClaimableJob()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateDueSchedule("schedule-a", priority: 7, groupKeys: ["group-a"]));
        await context.MakeScheduleDueAsync("schedule-a");

        (await context.Store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(DateTimeOffset.UtcNow, null))).ShouldBe(1);

        var claimed = await ClaimAsync(context.Store);
        claimed.SourceScheduleKey.ShouldBe("schedule-a");
        claimed.Priority.ShouldBe(7);
        claimed.ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        claimed.ScheduledFireAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task RecurringOverlap_Skip_DropsOccurrenceWhenEarlierOccurrenceIsNonTerminal()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateDueSchedule("schedule-a", overlapMode: RecurringOverlapMode.Skip));
        await context.MakeScheduleDueAsync("schedule-a");
        var firstMaterializedAtUtc = DateTimeOffset.UtcNow.AddMinutes(2);
        (await context.Store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(firstMaterializedAtUtc, null))).ShouldBe(1);

        await context.MakeScheduleDueAsync("schedule-a");
        (await context.Store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(firstMaterializedAtUtc.AddMinutes(2), null))).ShouldBe(0);

        (await ClaimAsync(context.Store)).SourceScheduleKey.ShouldBe("schedule-a");
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
    }

    [Fact]
    public async Task RecurringOverlap_Allow_MaterializesMultipleNonTerminalOccurrences()
    {
        await using var context = await this.CreateContextAsync();

        await context.Store.CreateOrUpdateRecurringScheduleAsync(CreateDueSchedule("schedule-a", overlapMode: RecurringOverlapMode.Allow));
        await context.MakeScheduleDueAsync("schedule-a");
        var firstMaterializedAtUtc = DateTimeOffset.UtcNow.AddMinutes(2);
        (await context.Store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(firstMaterializedAtUtc, null))).ShouldBe(1);

        await context.MakeScheduleDueAsync("schedule-a");
        (await context.Store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(firstMaterializedAtUtc.AddMinutes(2), null))).ShouldBe(1);

        (await ClaimAsync(context.Store)).SourceScheduleKey.ShouldBe("schedule-a");
        (await ClaimAsync(context.Store)).SourceScheduleKey.ShouldBe("schedule-a");
    }

    protected static async ValueTask<ClaimedJob> ClaimAsync(
        IJobStore store,
        string nodeId = "node-1",
        TimeSpan? leaseDuration = null)
    {
        return (await store.TryClaimNextAsync(CreateClaimRequest(nodeId, leaseDuration: leaseDuration)))
          .ShouldBeOfType<ClaimJobResult.Claimed>()
          .Job;
    }

    protected static ClaimJobRequest CreateClaimRequest(
        string nodeId,
        DateTimeOffset? claimedAtUtc = null,
        TimeSpan? leaseDuration = null)
    {
        var claimedAt = claimedAtUtc ?? DateTimeOffset.UtcNow;
        return new ClaimJobRequest(nodeId, claimedAt, claimedAt.Add(leaseDuration ?? TimeSpan.FromSeconds(30)));
    }

    protected static EnqueueJobRequest CreateRequest(
        Guid jobId,
        int priority = 0,
        DateTimeOffset? notBeforeUtc = null,
        int maxAttempts = 1,
        RetryBackoffKind? retryBackoffKind = null,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null,
        IReadOnlyList<string>? groupKeys = null)
      => new(
        jobId,
        priority,
        typeof(JobStoreContractService).AssemblyQualifiedName!,
        nameof(JobStoreContractService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        groupKeys ?? [],
        DateTimeOffset.UtcNow,
        notBeforeUtc,
        maxAttempts,
        retryBackoffKind,
        retryBaseDelay,
        retryMaxDelay);

    protected static UpsertRecurringScheduleRequest CreateSchedule(
        string scheduleKey,
        int priority = 0,
        IReadOnlyList<string>? groupKeys = null,
        RetryPolicy? retryPolicy = null,
        RecurringOverlapMode overlapMode = RecurringOverlapMode.Skip,
        DateTimeOffset? upsertedAtUtc = null)
      => new(
        scheduleKey,
        "* * * * *",
        typeof(JobStoreContractService).AssemblyQualifiedName!,
        nameof(JobStoreContractService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        priority,
        groupKeys ?? [],
        retryPolicy,
        overlapMode,
        upsertedAtUtc ?? DateTimeOffset.UtcNow);

    protected static UpsertRecurringScheduleRequest CreateDueSchedule(
        string scheduleKey,
        int priority = 0,
        IReadOnlyList<string>? groupKeys = null,
        RetryPolicy? retryPolicy = null,
        RecurringOverlapMode overlapMode = RecurringOverlapMode.Skip)
      => CreateSchedule(
        scheduleKey,
        priority,
        groupKeys,
        retryPolicy,
        overlapMode,
        DateTimeOffset.UtcNow.AddMinutes(-2));

    protected static JobFailureInfo CreateFailure()
      => new("TestException", "failed", null);

    protected static SerializedJobPayload EmptyPayload()
      => new(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray());

    private sealed class JobStoreContractService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
