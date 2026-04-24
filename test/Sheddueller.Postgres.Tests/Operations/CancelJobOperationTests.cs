namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Storage;

using Shouldly;

public sealed class CancelJobOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Cancel_QueuedJob_MarksCanceled()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(jobId));

        (await context.Store.CancelAsync(new CancelJobRequest(jobId, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.Canceled);

        var job = await context.ReadJobAsync(jobId);
        job.State.ShouldBe("Canceled");
        job.CanceledAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Cancel_ClaimedJob_RequestsCooperativeCancellation()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(jobId));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);

        (await context.Store.CancelAsync(new CancelJobRequest(jobId, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.CancellationRequested);

        var job = await context.ReadJobAsync(jobId);
        job.State.ShouldBe("Claimed");
        job.CancellationRequestedAtUtc.ShouldNotBeNull();
        (await context.Store.GetCancellationRequestedAtAsync(new JobCancellationStatusRequest(jobId, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldNotBeNull();

        var events = await ReadEventsAsync(context, jobId);
        events.Count(jobEvent => jobEvent.Kind == JobEventKind.CancelRequested).ShouldBe(1);

        (await context.Store.CancelAsync(new CancelJobRequest(jobId, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.CancellationRequested);
        events = await ReadEventsAsync(context, jobId);
        events.Count(jobEvent => jobEvent.Kind == JobEventKind.CancelRequested).ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_TerminalOrMissingJob_ReturnsExpectedResult()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var completed = Guid.NewGuid();
        var canceled = Guid.NewGuid();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(completed));
        var claimed = await PostgresTestData.ClaimAsync(context.Store);
        (await context.Store.MarkCompletedAsync(new CompleteJobRequest(completed, "node-1", claimed.LeaseToken, DateTimeOffset.UtcNow))).ShouldBeTrue();

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(canceled));
        (await context.Store.CancelAsync(new CancelJobRequest(canceled, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.Canceled);

        (await context.Store.CancelAsync(new CancelJobRequest(completed, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.AlreadyFinished);
        (await context.Store.CancelAsync(new CancelJobRequest(canceled, DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.AlreadyFinished);
        (await context.Store.CancelAsync(new CancelJobRequest(Guid.NewGuid(), DateTimeOffset.UtcNow))).ShouldBe(JobCancellationResult.NotFound);
    }

    private static async ValueTask<IReadOnlyList<JobEvent>> ReadEventsAsync(
        PostgresTestContext context,
        Guid jobId)
    {
        var inspectionReader = context.Store as IJobInspectionReader
          ?? throw new InvalidOperationException("Postgres store must provide job inspection.");
        var events = new List<JobEvent>();
        await foreach (var jobEvent in inspectionReader.ReadEventsAsync(jobId))
        {
            events.Add(jobEvent);
        }

        return events;
    }
}
