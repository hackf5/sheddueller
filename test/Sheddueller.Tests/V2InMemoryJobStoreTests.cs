namespace Sheddueller.Tests;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class V2InMemoryJobStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DelayedJob_FutureNotBefore_IsNotClaimableUntilDue()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(jobId, notBeforeUtc: Now.AddMinutes(1)));

        (await store.TryClaimNextAsync(CreateClaimRequest("node-1", Now))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
        var claimed = await ClaimJobAsync(store, Now.AddMinutes(1));

        claimed.JobId.ShouldBe(jobId);
    }

    [Fact]
    public async Task JobFailure_NoRetryPolicy_FailsTerminally()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(jobId));
        var claimed = await ClaimJobAsync(store, Now);

        (await store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", claimed.LeaseToken, Now, CreateFailure()))).ShouldBeTrue();

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.State.ShouldBe(JobState.Failed);
        snapshot.AttemptCount.ShouldBe(1);
        snapshot.Failure.ShouldNotBeNull().Message.ShouldBe("failed");
    }

    [Fact]
    public async Task JobFailure_FixedRetryPolicy_RequeuesWithExpectedNotBefore()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(
          jobId,
          maxAttempts: 3,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromSeconds(5)));

        var firstAttempt = await ClaimJobAsync(store, Now);
        (await store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", firstAttempt.LeaseToken, Now, CreateFailure()))).ShouldBeTrue();

        var afterFirstFailure = store.GetSnapshot(jobId).ShouldNotBeNull();
        afterFirstFailure.State.ShouldBe(JobState.Queued);
        afterFirstFailure.AttemptCount.ShouldBe(1);
        afterFirstFailure.NotBeforeUtc.ShouldBe(Now.AddSeconds(5));
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1", Now.AddSeconds(4)))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        var secondAttempt = await ClaimJobAsync(store, Now.AddSeconds(5));
        secondAttempt.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task JobFailure_ExponentialRetryPolicy_UsesCappedBackoff()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(
          jobId,
          maxAttempts: 3,
          retryBackoffKind: RetryBackoffKind.Exponential,
          retryBaseDelay: TimeSpan.FromSeconds(5),
          retryMaxDelay: TimeSpan.FromSeconds(8)));

        var firstAttempt = await ClaimJobAsync(store, Now);
        (await store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", firstAttempt.LeaseToken, Now, CreateFailure()))).ShouldBeTrue();
        var secondAttempt = await ClaimJobAsync(store, Now.AddSeconds(5));
        (await store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", secondAttempt.LeaseToken, Now.AddSeconds(5), CreateFailure()))).ShouldBeTrue();

        store.GetSnapshot(jobId).ShouldNotBeNull().NotBeforeUtc.ShouldBe(Now.AddSeconds(13));
    }

    [Fact]
    public async Task Heartbeat_ExtendedLease_PreventsRecoveryUntilExpiry()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(jobId));
        var claimed = await ClaimJobAsync(store, Now);

        (await store.RenewLeaseAsync(new RenewLeaseRequest(jobId, "node-1", claimed.LeaseToken, Now.AddSeconds(10), Now.AddSeconds(40)))).ShouldBeTrue();
        (await store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(Now.AddSeconds(35)))).ShouldBe(0);
        store.GetSnapshot(jobId).ShouldNotBeNull().State.ShouldBe(JobState.Claimed);

        (await store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(Now.AddSeconds(41)))).ShouldBe(1);
        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.State.ShouldBe(JobState.Failed);
        snapshot.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task LeaseOwner_StaleToken_CannotMutateReclaimedJob()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(
          jobId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromSeconds(1)));

        var staleClaim = await ClaimJobAsync(store, Now, "node-1");
        (await store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(Now.AddSeconds(31)))).ShouldBe(1);
        var currentClaim = await ClaimJobAsync(store, Now.AddSeconds(32), "node-2");

        (await store.MarkCompletedAsync(new CompleteJobRequest(jobId, "node-1", staleClaim.LeaseToken, Now.AddSeconds(32)))).ShouldBeFalse();
        (await store.MarkFailedAsync(new FailJobRequest(jobId, "node-1", staleClaim.LeaseToken, Now.AddSeconds(32), CreateFailure()))).ShouldBeFalse();
        (await store.RenewLeaseAsync(new RenewLeaseRequest(jobId, "node-1", staleClaim.LeaseToken, Now.AddSeconds(32), Now.AddSeconds(62)))).ShouldBeFalse();
        (await store.ReleaseJobAsync(new ReleaseJobRequest(jobId, "node-1", staleClaim.LeaseToken, Now.AddSeconds(32)))).ShouldBeFalse();

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.State.ShouldBe(JobState.Claimed);
        snapshot.LeaseToken.ShouldBe(currentClaim.LeaseToken);
    }

    [Fact]
    public async Task Cancel_QueuedJobOnly_CancelsBeforeClaim()
    {
        var store = new InMemoryJobStore();
        var queued = Guid.NewGuid();
        var claimed = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(queued));
        await store.EnqueueAsync(CreateRequest(claimed));

        (await store.CancelAsync(new CancelJobRequest(queued, Now))).ShouldBeTrue();
        store.GetSnapshot(queued).ShouldNotBeNull().State.ShouldBe(JobState.Canceled);

        var claimedJob = await ClaimJobAsync(store, Now);
        (await store.CancelAsync(new CancelJobRequest(claimedJob.JobId, Now))).ShouldBeFalse();
        store.GetSnapshot(claimedJob.JobId).ShouldNotBeNull().State.ShouldBe(JobState.Claimed);
    }

    [Fact]
    public async Task SchedulerInterruption_CurrentLeaseOwner_RequeuesWithoutRetryBudgetConsumption()
    {
        var store = new InMemoryJobStore();
        var jobId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(jobId, maxAttempts: 1));
        var claimed = await ClaimJobAsync(store, Now);

        (await store.ReleaseJobAsync(new ReleaseJobRequest(jobId, "node-1", claimed.LeaseToken, Now))).ShouldBeTrue();

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.State.ShouldBe(JobState.Queued);
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.LeaseToken.ShouldBeNull();
    }

    [Fact]
    public async Task RecurringSchedule_UpsertWhilePaused_PreservesPauseStateAndRecomputesOnResume()
    {
        var store = new InMemoryJobStore();

        (await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 0))).ShouldBe(RecurringScheduleUpsertResult.Created);
        (await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 0))).ShouldBe(RecurringScheduleUpsertResult.Unchanged);
        (await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 1))).ShouldBe(RecurringScheduleUpsertResult.Updated);

        (await store.PauseRecurringScheduleAsync("schedule-a", Now)).ShouldBeTrue();
        (await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 2))).ShouldBe(RecurringScheduleUpsertResult.Updated);

        var paused = await store.GetRecurringScheduleAsync("schedule-a");
        paused.ShouldNotBeNull().IsPaused.ShouldBeTrue();
        paused.NextFireAtUtc.ShouldBeNull();

        (await store.ResumeRecurringScheduleAsync("schedule-a", Now)).ShouldBeTrue();
        var resumed = await store.GetRecurringScheduleAsync("schedule-a");
        resumed.ShouldNotBeNull().IsPaused.ShouldBeFalse();
        resumed.NextFireAtUtc.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public async Task RecurringSchedule_Overdue_MaterializesOneCatchUpOccurrence()
    {
        var store = new InMemoryJobStore();
        await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a"));

        (await store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(Now.AddMinutes(5), null))).ShouldBe(1);

        var claimed = await ClaimJobAsync(store, Now.AddMinutes(5));
        claimed.SourceScheduleKey.ShouldBe("schedule-a");
        claimed.ScheduledFireAtUtc.ShouldBe(Now.AddMinutes(1));
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1", Now.AddMinutes(5)))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();

        var schedule = await store.GetRecurringScheduleAsync("schedule-a");
        schedule.ShouldNotBeNull().NextFireAtUtc.ShouldBe(Now.AddMinutes(6));
    }

    [Fact]
    public async Task RecurringOverlap_Skip_DropsOccurrenceWhenEarlierOccurrenceIsNonTerminal()
    {
        var store = new InMemoryJobStore();
        await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", overlapMode: RecurringOverlapMode.Skip));

        (await store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(Now.AddMinutes(1), null))).ShouldBe(1);
        (await store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(Now.AddMinutes(2), null))).ShouldBe(0);

        (await ClaimJobAsync(store, Now.AddMinutes(2))).SourceScheduleKey.ShouldBe("schedule-a");
        (await store.TryClaimNextAsync(CreateClaimRequest("node-1", Now.AddMinutes(2)))).ShouldBeOfType<ClaimJobResult.NoJobAvailable>();
    }

    [Fact]
    public async Task RecurringOverlap_Allow_MaterializesMultipleNonTerminalOccurrences()
    {
        var store = new InMemoryJobStore();
        await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", overlapMode: RecurringOverlapMode.Allow));

        (await store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(Now.AddMinutes(1), null))).ShouldBe(1);
        (await store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(Now.AddMinutes(2), null))).ShouldBe(1);

        (await ClaimJobAsync(store, Now.AddMinutes(2))).SourceScheduleKey.ShouldBe("schedule-a");
        (await ClaimJobAsync(store, Now.AddMinutes(2))).SourceScheduleKey.ShouldBe("schedule-a");
    }

    private static async Task<ClaimedJob> ClaimJobAsync(
        InMemoryJobStore store,
        DateTimeOffset claimedAtUtc,
        string nodeId = "node-1")
    {
        return (await store.TryClaimNextAsync(CreateClaimRequest(nodeId, claimedAtUtc)))
          .ShouldBeOfType<ClaimJobResult.Claimed>()
          .Job;
    }

    private static ClaimJobRequest CreateClaimRequest(string nodeId, DateTimeOffset claimedAtUtc)
      => new(nodeId, claimedAtUtc, claimedAtUtc.AddSeconds(30));

    private static EnqueueJobRequest CreateRequest(
        Guid jobId,
        DateTimeOffset? notBeforeUtc = null,
        int maxAttempts = 1,
        RetryBackoffKind? retryBackoffKind = null,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null)
      => new(
        jobId,
        0,
        typeof(StoreTestService).AssemblyQualifiedName!,
        nameof(StoreTestService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        [],
        Now,
        notBeforeUtc,
        maxAttempts,
        retryBackoffKind,
        retryBaseDelay,
        retryMaxDelay);

    private static UpsertRecurringScheduleRequest CreateSchedule(
        string scheduleKey,
        int priority = 0,
        RecurringOverlapMode overlapMode = RecurringOverlapMode.Skip)
      => new(
        scheduleKey,
        "* * * * *",
        typeof(StoreTestService).AssemblyQualifiedName!,
        nameof(StoreTestService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        priority,
        [],
        null,
        overlapMode,
        Now);

    private static JobFailureInfo CreateFailure()
      => new("TestException", "failed", null);

    private static SerializedJobPayload EmptyPayload()
      => new(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray());

    private sealed class StoreTestService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
