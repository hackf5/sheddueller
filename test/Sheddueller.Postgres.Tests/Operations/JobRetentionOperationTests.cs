namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Storage;

using Shouldly;

public sealed class JobRetentionOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task CleanupTerminalJobs_DeletedJob_RemovesCascadingRows()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(
          jobId,
          groupKeys: ["group-a"],
          tags: [new JobTag("tenant", "acme")]));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);
        await context.Store.MarkCompletedAsync(new CompleteJobRequest(
          jobId,
          "node-1",
          claimed.LeaseToken,
          DateTimeOffset.UtcNow.AddDays(-2)));

        (await context.ReadJobTagsAsync(jobId)).ShouldNotBeEmpty();
        (await context.ReadJobGroupKeysAsync(jobId)).ShouldNotBeEmpty();
        (await context.CountJobEventsAsync(jobId)).ShouldBeGreaterThan(0);

        var result = await ((IJobRetentionStore)context.Store).CleanupTerminalJobsAsync(
          new JobRetentionCleanupRequest(
            DateTimeOffset.UtcNow.AddDays(-1),
            failedBeforeUtc: null,
            canceledBeforeUtc: null,
            batchSize: 10));

        result.DeletedCount.ShouldBe(1);
        (await ((IJobInspectionReader)context.Store).GetJobAsync(jobId)).ShouldBeNull();
        (await context.ReadJobTagsAsync(jobId)).ShouldBeEmpty();
        (await context.ReadJobGroupKeysAsync(jobId)).ShouldBeEmpty();
        (await context.CountJobEventsAsync(jobId)).ShouldBe(0);
    }
}
