namespace Sheddueller.ProviderContracts;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public abstract class JobRetentionStoreContractTests
{
    protected abstract ValueTask<JobRetentionStoreContractContext> CreateRetentionContextAsync();

    [Fact]
    public async Task CleanupTerminalJobs_TerminalCutoffs_DeletesOnlyEligibleTerminalJobs()
    {
        await using var context = await this.CreateRetentionContextAsync();
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-10);
        var recent = now.AddHours(-1);

        var oldCompleted = await CompleteJobAsync(context.Store, old);
        var recentCompleted = await CompleteJobAsync(context.Store, recent);
        var oldFailed = await FailJobAsync(context.Store, old);
        var recentFailed = await FailJobAsync(context.Store, recent);
        var oldCanceled = await CancelJobAsync(context.Store, old);
        var recentCanceled = await CancelJobAsync(context.Store, recent);
        var oldQueued = await EnqueueJobAsync(context.Store, now.AddDays(-30), priority: -100);
        var oldClaimed = await EnqueueJobAsync(context.Store, now.AddDays(-30), priority: 100);

        (await ClaimAsync(context.Store)).JobId.ShouldBe(oldClaimed);

        var result = await context.RetentionStore.CleanupTerminalJobsAsync(
          new JobRetentionCleanupRequest(
            now.AddDays(-1),
            now.AddDays(-1),
            now.AddDays(-1),
            20));

        result.DeletedCount.ShouldBe(3);
        await AssertDeletedAsync(context.Reader, oldCompleted);
        await AssertDeletedAsync(context.Reader, oldFailed);
        await AssertDeletedAsync(context.Reader, oldCanceled);
        await AssertRetainedAsync(context.Reader, recentCompleted);
        await AssertRetainedAsync(context.Reader, recentFailed);
        await AssertRetainedAsync(context.Reader, recentCanceled);
        await AssertRetainedAsync(context.Reader, oldQueued);
        await AssertRetainedAsync(context.Reader, oldClaimed);
    }

    [Fact]
    public async Task CleanupTerminalJobs_BatchSize_DeletesOneBatchAtATime()
    {
        await using var context = await this.CreateRetentionContextAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var old = cutoff.AddDays(-1);

        var first = await CompleteJobAsync(context.Store, old);
        var second = await CompleteJobAsync(context.Store, old.AddMinutes(1));
        var third = await CompleteJobAsync(context.Store, old.AddMinutes(2));
        var request = new JobRetentionCleanupRequest(cutoff, failedBeforeUtc: null, canceledBeforeUtc: null, batchSize: 2);

        (await context.RetentionStore.CleanupTerminalJobsAsync(request)).DeletedCount.ShouldBe(2);
        (await context.RetentionStore.CleanupTerminalJobsAsync(request)).DeletedCount.ShouldBe(1);
        (await context.RetentionStore.CleanupTerminalJobsAsync(request)).DeletedCount.ShouldBe(0);

        await AssertDeletedAsync(context.Reader, first);
        await AssertDeletedAsync(context.Reader, second);
        await AssertDeletedAsync(context.Reader, third);
    }

    [Fact]
    public async Task CleanupTerminalJobs_NullCutoff_RetainsThatTerminalState()
    {
        await using var context = await this.CreateRetentionContextAsync();
        var oldCompleted = await CompleteJobAsync(context.Store, DateTimeOffset.UtcNow.AddDays(-10));

        var result = await context.RetentionStore.CleanupTerminalJobsAsync(
          new JobRetentionCleanupRequest(
            completedBeforeUtc: null,
            failedBeforeUtc: DateTimeOffset.UtcNow,
            canceledBeforeUtc: DateTimeOffset.UtcNow,
            batchSize: 20));

        result.DeletedCount.ShouldBe(0);
        await AssertRetainedAsync(context.Reader, oldCompleted);
    }

    private static async ValueTask<Guid> CompleteJobAsync(
        IJobStore store,
        DateTimeOffset completedAtUtc)
    {
        var jobId = await EnqueueJobAsync(store, completedAtUtc.AddMinutes(-1));
        var claimed = await ClaimAsync(store, "complete-node");
        claimed.JobId.ShouldBe(jobId);
        (await store.MarkCompletedAsync(new CompleteJobRequest(jobId, "complete-node", claimed.LeaseToken, completedAtUtc)))
          .ShouldBeTrue();
        return jobId;
    }

    private static async ValueTask<Guid> FailJobAsync(
        IJobStore store,
        DateTimeOffset failedAtUtc)
    {
        var jobId = await EnqueueJobAsync(store, failedAtUtc.AddMinutes(-1));
        var claimed = await ClaimAsync(store, "fail-node");
        claimed.JobId.ShouldBe(jobId);
        (await store.MarkFailedAsync(new FailJobRequest(jobId, "fail-node", claimed.LeaseToken, failedAtUtc, new JobFailureInfo("TestException", "failed", null))))
          .ShouldBeTrue();
        return jobId;
    }

    private static async ValueTask<Guid> CancelJobAsync(
        IJobStore store,
        DateTimeOffset canceledAtUtc)
    {
        var jobId = await EnqueueJobAsync(store, canceledAtUtc.AddMinutes(-1));
        (await store.CancelAsync(new CancelJobRequest(jobId, canceledAtUtc))).ShouldBe(JobCancellationResult.Canceled);
        return jobId;
    }

    private static async ValueTask<Guid> EnqueueJobAsync(
        IJobStore store,
        DateTimeOffset enqueuedAtUtc,
        int priority = 0)
    {
        var jobId = Guid.NewGuid();
        await store.EnqueueAsync(new EnqueueJobRequest(
          jobId,
          priority,
          typeof(JobRetentionContractService).AssemblyQualifiedName!,
          nameof(JobRetentionContractService.RunAsync),
          [typeof(CancellationToken).AssemblyQualifiedName!],
          new SerializedJobPayload(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
          ConcurrencyGroupKeys: [],
          enqueuedAtUtc,
          NotBeforeUtc: null,
          MaxAttempts: 1));
        return jobId;
    }

    private static async ValueTask<ClaimedJob> ClaimAsync(
        IJobStore store,
        string nodeId = "node-1")
    {
        var claimedAt = DateTimeOffset.UtcNow;
        return (await store.TryClaimNextAsync(new ClaimJobRequest(nodeId, claimedAt, claimedAt.AddMinutes(5))))
          .ShouldBeOfType<ClaimJobResult.Claimed>()
          .Job;
    }

    private static async ValueTask AssertDeletedAsync(
        IJobInspectionReader reader,
        Guid jobId)
      => (await reader.GetJobAsync(jobId)).ShouldBeNull();

    private static async ValueTask AssertRetainedAsync(
        IJobInspectionReader reader,
        Guid jobId)
      => (await reader.GetJobAsync(jobId)).ShouldNotBeNull();

    private sealed class JobRetentionContractService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
