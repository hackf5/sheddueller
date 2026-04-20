namespace Sheddueller.Tests;

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

using Shouldly;

public sealed class DashboardEndpointTests
{
    [Fact]
    public async Task MapShedduellerDashboard_ApplicationBranch_RoutesDashboardPagesUnderBranch()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDashboardJobReader, StubDashboardJobReader>();
        builder.Services.AddShedduellerDashboard();

        await using var app = builder.Build();
        ((IApplicationBuilder)app).MapShedduellerDashboard("/sheddueller");

        await app.StartAsync();

        var client = app.GetTestClient();

        var rootResponse = await client.GetAsync(new Uri("/sheddueller", UriKind.Relative));
        rootResponse.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        rootResponse.Headers.Location?.OriginalString.ShouldBe("/sheddueller/");

        var canonicalRootResponse = await client.GetAsync(new Uri("/sheddueller/", UriKind.Relative));
        canonicalRootResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var canonicalRootHtml = await canonicalRootResponse.Content.ReadAsStringAsync();
        canonicalRootHtml.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        canonicalRootHtml.ShouldContain("Operational Control");
        canonicalRootHtml.ShouldContain("Health Triage");
        canonicalRootHtml.ShouldContain("_framework/blazor.web.js");
        canonicalRootHtml.ShouldContain("prefers-color-scheme: dark");
        canonicalRootHtml.ShouldContain("#0b1326");
        canonicalRootHtml.ShouldContain("sd-live-status");
        canonicalRootHtml.ShouldContain("Updated");
        canonicalRootHtml.ShouldContain("Running Jobs");
        canonicalRootHtml.ShouldContain("Recently Failed");
        canonicalRootHtml.ShouldContain("Queued (Next Up)");
        canonicalRootHtml.ShouldContain($"href=\"jobs/{StubDashboardJobReader.JobId:D}\"");

        var jobsResponse = await client.GetAsync(new Uri("/sheddueller/jobs", UriKind.Relative));
        jobsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var jobsHtml = await jobsResponse.Content.ReadAsStringAsync();
        jobsHtml.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        jobsHtml.ShouldContain("Query Parameters");
        jobsHtml.ShouldContain("Search Results");
        jobsHtml.ShouldContain("sd-live-status");
        jobsHtml.ShouldContain("Auto-refresh: On");
        jobsHtml.ShouldContain("Execute Query");
        jobsHtml.ShouldContain($"href=\"jobs/{StubDashboardJobReader.JobId:D}\"");
        jobsHtml.ShouldNotContain("@onclick");
        jobsHtml.ShouldNotContain("@onsubmit");

        var detailResponse = await client.GetAsync(new Uri($"/sheddueller/jobs/{StubDashboardJobReader.JobId:D}", UriKind.Relative));
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detailHtml = await detailResponse.Content.ReadAsStringAsync();
        detailHtml.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        detailHtml.ShouldContain(StubDashboardJobReader.JobId.ToString("D"));
    }

    private sealed class StubDashboardJobReader : IDashboardJobReader
    {
        public static readonly Guid JobId = Guid.Parse("8c32d457-9e7a-42bb-8947-0c8fa54743be");

        private static readonly DashboardJobSummary Job = new(
          JobId,
          JobState.Queued,
          "Sheddueller.Tests.DashboardEndpointTests.StubService",
          "Run",
          Priority: 0,
          EnqueueSequence: 1,
          EnqueuedAtUtc: DateTimeOffset.Parse("2026-04-20T12:00:00Z", CultureInfo.InvariantCulture),
          NotBeforeUtc: null,
          AttemptCount: 0,
          MaxAttempts: 3,
          Tags: [],
          SourceScheduleKey: null,
          LatestProgress: null,
          new DashboardQueuePosition(JobId, DashboardQueuePositionKind.Claimable, Position: 1),
          CompletedAtUtc: null,
          FailedAtUtc: null,
          CanceledAtUtc: null);

        private static readonly DashboardJobOverview Overview = new(
          new Dictionary<JobState, int>(),
          [],
          [],
          [Job],
          [],
          []);

        public ValueTask<DashboardJobOverview> GetOverviewAsync(
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(Overview);

        public ValueTask<DashboardJobPage> SearchJobsAsync(
            DashboardJobQuery query,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(new DashboardJobPage([Job], ContinuationToken: null));

        public ValueTask<DashboardJobDetail?> GetJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<DashboardJobDetail?>(
            jobId == JobId
              ? new DashboardJobDetail(
                Job,
                ClaimedAtUtc: null,
                ClaimedByNodeId: null,
                LeaseExpiresAtUtc: null,
                ScheduledFireAtUtc: null,
                RecentEvents: [])
              : null);

        public ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(new DashboardQueuePosition(jobId, DashboardQueuePositionKind.NotFound, Position: null, "Job was not found."));

        public IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
            Guid jobId,
            DashboardEventQuery? query = null,
            CancellationToken cancellationToken = default)
          => ReadNoEventsAsync(cancellationToken);

        private static async IAsyncEnumerable<DashboardJobEvent> ReadNoEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }
}
