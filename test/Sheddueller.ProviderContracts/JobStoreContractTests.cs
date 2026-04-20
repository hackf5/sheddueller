namespace Sheddueller.ProviderContracts;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public abstract class TaskStoreContractTests
{
    protected static readonly DateTimeOffset ContractClock = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    protected abstract ValueTask<TaskStoreContractContext> CreateContextAsync();

    [Fact]
    public async Task EnqueuedTask_Claim_RoundTripsSubmittedMetadata()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();
        var request = CreateRequest(
          taskId,
          priority: 7,
          maxAttempts: 3,
          retryBackoffKind: RetryBackoffKind.Exponential,
          retryBaseDelay: TimeSpan.FromMilliseconds(50),
          retryMaxDelay: TimeSpan.FromMilliseconds(75),
          groupKeys: ["alpha", "beta"]);

        var enqueueResult = await context.Store.EnqueueAsync(request);
        var claimed = await ClaimAsync(context.Store);

        enqueueResult.TaskId.ShouldBe(taskId);
        enqueueResult.EnqueueSequence.ShouldBeGreaterThan(0);
        claimed.TaskId.ShouldBe(taskId);
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

        (await ClaimAsync(context.Store)).TaskId.ShouldBe(high);
        (await ClaimAsync(context.Store)).TaskId.ShouldBe(firstLow);
        (await ClaimAsync(context.Store)).TaskId.ShouldBe(secondLow);
    }

    [Fact]
    public async Task DelayedTask_FutureNotBefore_IsNotClaimableUntilDue()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();
        var dueAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(250);

        await context.Store.EnqueueAsync(CreateRequest(taskId, notBeforeUtc: dueAtUtc));

        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        (await ClaimAsync(context.Store)).TaskId.ShouldBe(taskId);
    }

    [Fact]
    public async Task TryClaimNext_ConcurrentAttempts_ClaimsTaskOnlyOnce()
    {
        await using var context = await this.CreateContextAsync();
        await context.Store.EnqueueAsync(CreateRequest(Guid.NewGuid()));

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
          .Select(index => context.Store.TryClaimNextAsync(CreateClaimRequest($"node-{index}")).AsTask()));

        results.Count(result => result is ClaimTaskResult.Claimed).ShouldBe(1);
        results.Count(result => result is ClaimTaskResult.NoTaskAvailable).ShouldBe(9);
    }

    [Fact]
    public async Task ConcurrencyGroups_SaturatedGroup_SkipsOnlyBlockedTasks()
    {
        await using var context = await this.CreateContextAsync();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await ClaimAsync(context.Store)).TaskId.ShouldBe(running);

        await context.Store.EnqueueAsync(CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await context.Store.EnqueueAsync(CreateRequest(eligible, priority: 0));

        (await ClaimAsync(context.Store)).TaskId.ShouldBe(eligible);
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
        firstClaim.TaskId.ShouldBe(first);
        var secondClaim = await ClaimAsync(context.Store);
        secondClaim.TaskId.ShouldBe(second);

        await context.Store.SetConcurrencyLimitAsync(new SetConcurrencyLimitRequest("critical", 1, ContractClock));
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(first, "node-1", firstClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(second, "node-1", secondClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();
        (await ClaimAsync(context.Store)).TaskId.ShouldBe(waiting);
    }

    [Fact]
    public async Task MarkCompleted_CurrentLease_RemovesTaskFromClaimableWork()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(taskId));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
    }

    [Fact]
    public async Task MarkFailed_NoRetryPolicy_FailsTerminally()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(taskId));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
    }

    [Fact]
    public async Task MarkFailed_FixedRetryPolicy_RequeuesAfterBackoff()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          taskId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(250)));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeTrue();
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        var retry = await ClaimAsync(context.Store);
        retry.TaskId.ShouldBe(taskId);
        retry.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task LeaseOwner_StaleToken_CannotMutateReclaimedTask()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(
          taskId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(1)));

        var staleClaim = await ClaimAsync(context.Store, "node-1", TimeSpan.FromMilliseconds(150));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        (await context.Store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(DateTimeOffset.UtcNow))).ShouldBe(1);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        var currentClaim = await ClaimAsync(context.Store, "node-2");

        (await context.Store.MarkCompletedAsync(new CompleteTaskRequest(taskId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeFalse();
        (await context.Store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow, CreateFailure()))).ShouldBeFalse();
        (await context.Store.RenewLeaseAsync(new RenewLeaseRequest(taskId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30)))).ShouldBeFalse();
        (await context.Store.ReleaseTaskAsync(new ReleaseTaskRequest(taskId, "node-1", staleClaim.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeFalse();
        currentClaim.LeaseToken.ShouldNotBe(staleClaim.LeaseToken);
    }

    [Fact]
    public async Task ReleaseTask_CurrentLease_RequeuesWithoutRetryBudgetConsumption()
    {
        await using var context = await this.CreateContextAsync();
        var taskId = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(taskId));
        var claimed = await ClaimAsync(context.Store);

        (await context.Store.ReleaseTaskAsync(new ReleaseTaskRequest(taskId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var reclaimed = await ClaimAsync(context.Store);
        reclaimed.TaskId.ShouldBe(taskId);
        reclaimed.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_QueuedTaskOnly_CancelsBeforeClaim()
    {
        await using var context = await this.CreateContextAsync();
        var queued = Guid.NewGuid();
        var claimed = Guid.NewGuid();

        await context.Store.EnqueueAsync(CreateRequest(queued));
        await context.Store.EnqueueAsync(CreateRequest(claimed));

        (await context.Store.CancelAsync(new CancelTaskRequest(queued, DateTimeOffset.UtcNow))).ShouldBeTrue();

        var claimedTask = await ClaimAsync(context.Store);
        (await context.Store.CancelAsync(new CancelTaskRequest(claimedTask.TaskId, DateTimeOffset.UtcNow))).ShouldBeFalse();
        claimedTask.TaskId.ShouldBe(claimed);
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
    public async Task RecurringSchedule_DueOccurrence_MaterializesClaimableTask()
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
        (await context.Store.TryClaimNextAsync(CreateClaimRequest("node-1"))).ShouldBeOfType<ClaimTaskResult.NoTaskAvailable>();
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

    protected static async ValueTask<ClaimedTask> ClaimAsync(
        ITaskStore store,
        string nodeId = "node-1",
        TimeSpan? leaseDuration = null)
    {
        return (await store.TryClaimNextAsync(CreateClaimRequest(nodeId, leaseDuration: leaseDuration)))
          .ShouldBeOfType<ClaimTaskResult.Claimed>()
          .Task;
    }

    protected static ClaimTaskRequest CreateClaimRequest(
        string nodeId,
        DateTimeOffset? claimedAtUtc = null,
        TimeSpan? leaseDuration = null)
    {
        var claimedAt = claimedAtUtc ?? DateTimeOffset.UtcNow;
        return new ClaimTaskRequest(nodeId, claimedAt, claimedAt.Add(leaseDuration ?? TimeSpan.FromSeconds(30)));
    }

    protected static EnqueueTaskRequest CreateRequest(
        Guid taskId,
        int priority = 0,
        DateTimeOffset? notBeforeUtc = null,
        int maxAttempts = 1,
        RetryBackoffKind? retryBackoffKind = null,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null,
        IReadOnlyList<string>? groupKeys = null)
      => new(
        taskId,
        priority,
        typeof(TaskStoreContractService).AssemblyQualifiedName!,
        nameof(TaskStoreContractService.RunAsync),
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
        typeof(TaskStoreContractService).AssemblyQualifiedName!,
        nameof(TaskStoreContractService.RunAsync),
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

    protected static TaskFailureInfo CreateFailure()
      => new("TestException", "failed", null);

    protected static SerializedTaskPayload EmptyPayload()
      => new(SystemTextJsonTaskPayloadSerializer.JsonContentType, "[]"u8.ToArray());

    private sealed class TaskStoreContractService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
