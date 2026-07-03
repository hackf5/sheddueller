namespace Sheddueller.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Sheddueller.Runtime;

using Shouldly;

public sealed class JobManagerTests
{
    [Fact]
    public async Task CancelQueuedJobs_CurrentTime_PassesRequestToStoreAndReturnsCount()
    {
        var now = new DateTimeOffset(2026, 4, 20, 12, 30, 0, TimeSpan.Zero);
        var store = new RecordingJobStore
        {
            CancelQueuedJobsResult = 12,
        };
        var manager = new JobManager(store, new FakeTimeProvider(now), NullLogger<JobManager>.Instance);

        var canceledCount = await manager.CancelQueuedJobsAsync();

        canceledCount.ShouldBe(12);
        store.CancelQueuedJobsRequests.ShouldHaveSingleItem().CanceledAtUtc.ShouldBe(now);
    }
}
