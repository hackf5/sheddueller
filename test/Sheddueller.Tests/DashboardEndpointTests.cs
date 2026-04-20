namespace Sheddueller.Tests;

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
    public async Task MapShedduellerDashboard_ApplicationBranch_RendersDashboardRoot()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDashboardJobReader, EmptyDashboardJobReader>();
        builder.Services.AddShedduellerDashboard();

        await using var app = builder.Build();
        ((IApplicationBuilder)app).MapShedduellerDashboard("/sheddueller");

        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync(new Uri("/sheddueller", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Sheddueller");
    }

    private sealed class EmptyDashboardJobReader : IDashboardJobReader
    {
        private static readonly DashboardJobOverview Overview = new(
          new Dictionary<JobState, int>(),
          [],
          [],
          [],
          [],
          []);

        public ValueTask<DashboardJobOverview> GetOverviewAsync(
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(Overview);

        public ValueTask<DashboardJobPage> SearchJobsAsync(
            DashboardJobQuery query,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(new DashboardJobPage([], ContinuationToken: null));

        public ValueTask<DashboardJobDetail?> GetJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<DashboardJobDetail?>(null);

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
