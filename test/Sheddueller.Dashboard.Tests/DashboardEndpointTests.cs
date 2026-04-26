namespace Sheddueller.Dashboard.Tests;

using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Sheddueller;
using Sheddueller.Dashboard.Internal;
using Sheddueller.Inspection.ConcurrencyGroups;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Inspection.Metrics;
using Sheddueller.Inspection.Nodes;
using Sheddueller.Inspection.Schedules;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

using SchedulesPage = Sheddueller.Dashboard.Components.Pages.Schedules;

public sealed class DashboardEndpointTests
{
    [Fact]
    public async Task MapShedduellerDashboard_MinimalHosting_RedirectsToCanonicalRoot()
    {
        await using var app = await CreateStartedDashboardAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync(new Uri("/sheddueller", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/sheddueller/");
    }

    [Fact]
    public async Task MapShedduellerDashboard_ApplicationBranch_RedirectsToCanonicalRoot()
    {
        await using var app = await CreateStartedDashboardAsync(mapWithWebApplication: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync(new Uri("/sheddueller", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/sheddueller/");
    }

    [Fact]
    public async Task MapShedduellerDashboard_DefaultOptions_DoesNotPrerenderRouteContent()
    {
        await using var app = await CreateStartedDashboardAsync(prerender: false);
        var html = await GetOkHtmlAsync(app, "/sheddueller/");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("_framework/blazor.web.js");
        html.ShouldContain("_content/Sheddueller.Dashboard/vendor/prism/prism-dark.css\" rel=\"stylesheet\" media=\"(prefers-color-scheme: dark)\"");
        html.ShouldNotContain("Operational Control");
    }

    [Fact]
    public async Task Overview_KnownData_RendersOperationalSummary()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Operational Control");
        html.ShouldContain("Overview");
        html.ShouldContain("_framework/blazor.web.js");
        html.ShouldContain("prefers-color-scheme: dark");
        html.ShouldContain("#0b1326");
        AssertShellRefresh(html);
        html.ShouldContain("Running Jobs");
        html.ShouldContain("Recently Failed");
        html.ShouldContain("Queued (Next Up)");
        html.ShouldContain("Started");
        html.ShouldContain("Terminated");
        html.ShouldContain("Tags");
        html.ShouldContain("Error");
        html.ShouldNotContain("<th>State</th>");
        html.ShouldContain("tenant:acme");
        html.ShouldContain("schedule:daily_rollup");
        html.ShouldContain("2026-04-20 12:05:00 UTC");
        html.ShouldContain("2026-04-20 12:04:00 UTC");
        html.ShouldContain("href=\"jobs?state=Claimed\"");
        html.ShouldContain("href=\"jobs?state=Failed\"");
        html.ShouldContain("href=\"jobs?handler=StubService.Run\"");
        html.ShouldContain("href=\"jobs?tag=tenant%3Aacme\"");
        html.ShouldContain($"href=\"jobs/{StubJobInspectionReader.JobId:D}\"");
    }

    [Fact]
    public async Task Jobs_KnownData_RendersSearchResults()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/jobs");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Jobs");
        html.ShouldContain("Job filters");
        html.ShouldContain("Filter by status");
        html.ShouldContain("Filter by handler substring");
        html.ShouldContain("Filter by tag substring");
        html.ShouldContain("Filter by concurrency group substring");
        html.ShouldContain("Sort jobs");
        html.ShouldContain("Operational Order");
        html.ShouldContain("Newest First");
        html.ShouldContain("Clear Filters");
        html.ShouldNotContain("Expand query filters");
        html.ShouldNotContain("Execute Query");
        html.ShouldNotContain("Query Parameters");
        AssertAppearsBefore(html, "aria-label=\"Filter by status\"", "aria-label=\"Filter by handler substring\"");
        html.ShouldContain("Tags");
        html.ShouldContain("tenant:acme");
        html.ShouldContain("schedule:daily_rollup");
        html.ShouldContain("Groups");
        html.ShouldContain("tenant-acme");
        html.ShouldContain("daily-rollup");
        html.ShouldContain("href=\"jobs?state=Claimed\"");
        html.ShouldContain("href=\"jobs?handler=StubService.Run\"");
        html.ShouldContain("href=\"jobs?tag=tenant%3Aacme\"");
        html.ShouldContain("href=\"jobs?group=tenant-acme\"");
        AssertShellRefresh(html);
        html.ShouldContain($"href=\"jobs/{StubJobInspectionReader.JobId:D}\"");
        html.ShouldContain("<th>Queue</th>");
        html.ShouldContain("Running");
        html.ShouldContain("#1");
    }

    [Fact]
    public async Task Jobs_ClaimedAndQueuedFilters_RendersClaimedBeforeQueued()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/jobs?state=Queued&state=Claimed");

        AssertStatusCheckbox(html, "Queued", isChecked: true);
        AssertStatusCheckbox(html, "Claimed", isChecked: true);
        AssertAppearsBefore(html, $"href=\"jobs/{StubJobInspectionReader.JobId:D}\"", $"href=\"jobs/{StubJobInspectionReader.QueuedJobId:D}\"");
        html.ShouldContain("Running");
        html.ShouldContain("#1");
    }

    [Fact]
    public async Task Jobs_SortQuery_RendersSelectedSortAndPreservesQuickLinks()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/jobs?state=Queued&sort=NewestFirst");

        AssertSelectValue(html, "Sort jobs", "NewestFirst");
        html.ShouldContain("href=\"jobs?state=Claimed&amp;sort=NewestFirst\"");
        html.ShouldContain("href=\"jobs?state=Queued&amp;handler=StubService.Run&amp;sort=NewestFirst\"");
    }

    [Fact]
    public async Task Jobs_TagDisplayOrder_ConfiguredNamesLeadCompactChips()
    {
        await using var app = await CreateStartedDashboardAsync(configureDashboard: options =>
        {
            options.TagDisplayOrder = ["domain", "tenant"];
        });
        var html = await GetOkHtmlAsync(app, "/sheddueller/jobs");

        AssertAppearsBefore(html, "href=\"jobs?tag=domain%3Apayments\"", "href=\"jobs?tag=tenant%3Aacme\"");
        html.ShouldContain("jobs-chip--overflow");
        html.ShouldContain("aria-haspopup=\"true\"");
        html.ShouldContain("&#x2B;2");
        html.ShouldContain("sd-chip-overflow__panel");
        html.ShouldContain("href=\"jobs?tag=schedule%3Adaily_rollup\"");
        html.ShouldContain("href=\"jobs?tag=source%3Astub\"");
        html.ShouldContain("aria-label=\"Additional tags: domain:payments, tenant:acme, schedule:daily_rollup, source:stub\"");
    }

    [Fact]
    public async Task Jobs_QueryFilters_RendersControlsAndPreservingQuickLinks()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/jobs?state=Failed&handler=StubService.Run&tag=tenant%3Aacme&group=tenant-acme");

        AssertStatusCheckbox(html, "Failed", isChecked: true);
        AssertStatusCheckbox(html, "Queued", isChecked: false);
        html.ShouldContain("value=\"StubService.Run\"");
        html.ShouldContain("value=\"tenant:acme\"");
        html.ShouldContain("value=\"tenant-acme\"");
        html.ShouldContain("href=\"jobs?state=Claimed&amp;handler=StubService.Run&amp;tag=tenant%3Aacme&amp;group=tenant-acme\"");
        html.ShouldContain("href=\"jobs?state=Failed&amp;handler=StubService.Run&amp;tag=tenant%3Aacme&amp;group=tenant-acme\"");
        html.ShouldContain("href=\"jobs?state=Failed&amp;handler=StubService.Run&amp;tag=schedule%3Adaily_rollup&amp;group=tenant-acme\"");
        html.ShouldContain("href=\"jobs?state=Failed&amp;handler=StubService.Run&amp;tag=tenant%3Aacme&amp;group=daily-rollup\"");
    }

    [Fact]
    public async Task Schedules_KnownData_RendersRegistry()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/schedules");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Schedules");
        html.ShouldContain("Manage recurring operational tasks and cron triggers.");
        html.ShouldContain("Schedule Registry");
        html.ShouldContain("sd-table-shell");
        html.ShouldContain("sd-table-controls");
        html.ShouldContain("sd-table-search");
        html.ShouldContain("sd-table-select schedules-state-filter");
        html.ShouldContain("Filter by exact schedule key");
        html.ShouldContain("Filter by pause state");
        AssertAppearsBefore(html, "aria-label=\"Filter by pause state\"", "aria-label=\"Filter by exact schedule key\"");
        html.ShouldContain("etl_nightly_sync");
        html.ShouldContain("cache_eviction_hourly");
        html.ShouldContain("billing_reconciliation");
        html.ShouldContain("Sheddueller.Dashboard.Tests.Schedules.EtlSyncWorker.Run");
        html.ShouldContain("0 2 * * *");
        html.ShouldContain("Active");
        html.ShouldContain("Paused");
        html.ShouldContain("Skip");
        html.ShouldContain("Allow");
        html.ShouldContain("etl-nodes");
        html.ShouldContain("finance-secure");
        html.ShouldContain("tenant:acme");
        html.ShouldContain("schedule:nightly");
        html.ShouldContain("2026-04-20 12:03:00 UTC");
        html.ShouldContain("Trigger Now etl_nightly_sync");
        html.ShouldContain("Trigger Now cache_eviction_hourly");
        html.ShouldContain("Pause Schedule etl_nightly_sync");
        html.ShouldContain("Resume Schedule cache_eviction_hourly");
        html.ShouldContain("Load More Records");
        html.ShouldContain("Showing 1-3 of 3 schedules with more available");
        AssertShellRefresh(html);
    }

    [Fact]
    public void Schedules_TriggerActionMessages_FormatSuccessSkippedAndMissingCases()
    {
        SchedulesPage.CreateTriggerSuccessMessage("etl_nightly_sync")
          .ShouldBe("Schedule etl_nightly_sync triggered as job");
        SchedulesPage.CreateTriggerSkippedMessage("etl_nightly_sync")
          .ShouldBe("Schedule etl_nightly_sync already has an active occurrence.");
        SchedulesPage.ScheduleNotFoundActionFailureMessage
          .ShouldBe("Schedule action failed: Schedule was not found.");
    }

    [Fact]
    public async Task ConcurrencyGroups_KnownData_RendersRegistry()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/concurrency-groups");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Concurrency Groups");
        html.ShouldContain("Identify saturation and blocked work across resource pools.");
        html.ShouldContain("Group Registry");
        html.ShouldContain("sd-table-shell");
        html.ShouldContain("sd-table-controls");
        html.ShouldContain("sd-table-search");
        html.ShouldContain("sd-table-switch");
        html.ShouldContain("Filter by group key");
        html.ShouldContain("Saturated only");
        html.ShouldContain("Has blocked jobs");
        html.ShouldContain("type=\"checkbox\"");
        AssertAppearsBefore(html, "Saturated only", "aria-label=\"Filter by group key\"");
        AssertAppearsBefore(html, "Has blocked jobs", "aria-label=\"Filter by group key\"");
        html.ShouldContain("pool_etl_heavy");
        html.ShouldContain("api_sync_workers");
        html.ShouldContain("bg_maintenance");
        html.ShouldContain("db_vacuum_ops");
        html.ShouldContain("Saturated");
        html.ShouldContain("High Load");
        html.ShouldContain("Nominal");
        html.ShouldContain("Blocked Work");
        html.ShouldContain("2026-04-20 12:02:01 UTC");
        html.ShouldContain("Load More Records");
        html.ShouldContain("Showing 1-4 of 4 groups with more available");
        AssertShellRefresh(html);
    }

    [Fact]
    public async Task Nodes_KnownData_RendersRegistry()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/nodes");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Nodes");
        html.ShouldContain("Real-time liveness monitoring and local capacity tracking across the cluster.");
        html.ShouldContain("Total Nodes");
        html.ShouldContain("cluster-wide");
        html.ShouldContain("Active");
        html.ShouldContain("Stale");
        html.ShouldContain("Dead");
        html.ShouldContain("nodes-summary-card nodes-summary-card--dead");
        html.ShouldContain("Node Registry");
        html.ShouldContain("sd-table-shell");
        html.ShouldContain("sd-table-controls");
        html.ShouldContain("sd-table-search");
        html.ShouldContain("sd-table-select nodes-health-filter");
        html.ShouldContain("Filter node ID");
        html.ShouldContain("Filter by health state");
        AssertAppearsBefore(html, "aria-label=\"Filter by health state\"", "aria-label=\"Filter node ID\"");
        html.ShouldContain("wrk-prod-us-east-1a-8f92");
        html.ShouldContain("wrk-prod-eu-west-1a-7b22");
        html.ShouldContain("wrk-prod-ap-south-1b-1f00");
        html.ShouldContain("2026-04-20 12:00:00 UTC");
        html.ShouldContain("2026-04-20 12:04:30 UTC");
        html.ShouldContain("14,205");
        html.ShouldContain("3 / 4");
        html.ShouldContain("Load More Records");
        html.ShouldContain("Showing 1-3 of 3 nodes");
        AssertShellRefresh(html);
    }

    [Fact]
    public async Task Metrics_KnownData_RendersRollingHealth()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, "/sheddueller/metrics");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Metrics");
        html.ShouldContain("Live telemetry and comparative metrics for active scheduler nodes.");
        html.ShouldContain("5m");
        html.ShouldContain("1h");
        html.ShouldContain("24h");
        html.ShouldContain("metrics-window-tabs__button--selected");
        html.ShouldContain("Queue Depth");
        html.ShouldContain("Throughput Rate");
        html.ShouldContain("Schedule Fire Lag");
        html.ShouldContain("Live Throughput");
        html.ShouldContain("1s Buckets / 1h Window");
        html.ShouldContain("aria-label=\"Throughput series filters\"");
        html.ShouldContain("aria-pressed=\"true\"");
        html.ShouldContain("Failed Attempts");
        html.ShouldContain("Queue Latency");
        html.ShouldContain("Execution Duration");
        html.ShouldContain("Window Comparison");
        html.ShouldContain("Concurrency Saturation");
        html.ShouldContain("1,204");
        html.ShouldContain("842.3/min");
        html.ShouldContain("120 ms");
        html.ShouldContain("1.2 s");
        html.ShouldContain("2 saturated");
        AssertShellRefresh(html);
    }

    [Fact]
    public async Task JobDetail_KnownJob_RendersDetailAndDefaultLogFilter()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, $"/sheddueller/jobs/{StubJobInspectionReader.JobId:D}");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Job Detail");
        html.ShouldContain(StubJobInspectionReader.JobId.ToString("D"));
        html.ShouldContain("CLAIMED");
        html.ShouldContain("Progress");
        html.ShouldContain("45% Processing records chunk 4 of 9");
        html.ShouldContain("Metadata");
        html.ShouldContain("tenant:acme");
        html.ShouldContain("tenant-acme");
        html.ShouldContain("href=\"jobs?state=Claimed\"");
        html.ShouldContain("href=\"jobs?handler=StubService.Run\"");
        html.ShouldContain("href=\"jobs?tag=tenant%3Aacme\"");
        html.ShouldContain("href=\"jobs?group=tenant-acme\"");
        html.ShouldContain("Invocation");
        html.ShouldContain("pre.job-detail-invocation-call[class*=\"language-\"]");
        html.ShouldContain("box-shadow: none;");
        html.ShouldContain("StubService.Run(");
        html.ShouldContain("permanent-failure");
        html.ShouldContain("Job.Resolve");
        html.ShouldContain("StubDependency");
        html.ShouldContain("Job.Context");
        html.ShouldContain("scheduler-owned");
        html.ShouldContain("Lifecycle Timeline");
        html.ShouldContain("Attempt 1 Failed");
        html.ShouldContain("ConnectionTimeoutError: db-primary cluster unreachable");
        html.ShouldContain("Log Stream");
        html.ShouldContain("LIVE");
        html.ShouldContain("Updated ");
        html.ShouldContain("Show Progress");
        html.ShouldContain("type=\"checkbox\"");
        html.ShouldContain("Initializing worker environment");
        html.ShouldNotContain("progress event row should be hidden");
        AssertCancelButton(
          html,
          "Cancel Job",
          "Request cooperative cancellation for this running job.",
          disabled: false);
    }

    [Fact]
    public async Task JobDetail_QueuedJob_RendersEnabledCancelAction()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, $"/sheddueller/jobs/{StubJobInspectionReader.QueuedJobId:D}");

        AssertCancelButton(
          html,
          "Cancel Job",
          "Cancel this queued job before it is claimed.",
          disabled: false);
    }

    [Fact]
    public async Task JobDetail_CancellationRequestedJob_RendersDisabledCancelAction()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, $"/sheddueller/jobs/{StubJobInspectionReader.CancellationRequestedJobId:D}");

        AssertCancelButton(
          html,
          "Cancellation Requested",
          "Cancellation was already requested at 2026-04-20 12:08:30 UTC; the worker will observe it on heartbeat.",
          disabled: true);
    }

    [Fact]
    public async Task JobDetail_CompletedJob_RendersRunTime()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, $"/sheddueller/jobs/{StubJobInspectionReader.CompletedJobId:D}");

        html.ShouldContain("Run Time:");
        html.ShouldContain("4 m");
        html.ShouldContain("2026-04-20 12:09:00 UTC");
    }

    [Theory]
    [InlineData("a1543a1d-b7e0-4b43-b7ed-62249dc117be", "This job completed at 2026-04-20 12:09:00 UTC and cannot be canceled.")]
    [InlineData("b4f8131d-a097-4410-8f47-8d37387e1357", "This job failed at 2026-04-20 12:04:00 UTC and cannot be canceled.")]
    [InlineData("d271e83d-5657-4a4f-9644-dd3b07911efe", "This job was canceled at 2026-04-20 12:02:00 UTC and cannot be canceled again.")]
    public async Task JobDetail_TerminalJob_RendersDisabledCancelAction(
        string jobIdText,
        string expectedTitle)
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, $"/sheddueller/jobs/{jobIdText}");

        AssertCancelButton(
          html,
          "Cancel Job",
          expectedTitle,
          disabled: true);
    }

    [Fact]
    public async Task JobDetail_MissingJob_RendersNotFoundWithDisabledCancelAction()
    {
        await using var app = await CreateStartedDashboardAsync();
        var html = await GetOkHtmlAsync(app, $"/sheddueller/jobs/{StubJobInspectionReader.MissingJobId:D}");

        html.ShouldContain("base href=\"http://localhost/sheddueller/\"");
        html.ShouldContain("Job Detail");
        html.ShouldContain("Job Not Found");
        html.ShouldContain("The requested execution record could not be located.");
        html.ShouldContain(StubJobInspectionReader.MissingJobId.ToString("D"));
        html.ShouldContain("Return to Jobs");
        html.ShouldContain("href=\"jobs\"");
        AssertCancelButton(
          html,
          "Cancel Job",
          "No job was found with this ID.",
          disabled: true);
    }

    private static async Task<WebApplication> CreateStartedDashboardAsync(
        bool prerender = true,
        bool mapWithWebApplication = true,
        Action<ShedduellerDashboardOptions>? configureDashboard = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IJobInspectionReader, StubJobInspectionReader>();
        builder.Services.AddSingleton<IJobManager, StubJobManager>();
        builder.Services.AddSingleton<StubScheduleInspectionReader>();
        builder.Services.AddSingleton<IScheduleInspectionReader>(serviceProvider => serviceProvider.GetRequiredService<StubScheduleInspectionReader>());
        builder.Services.AddSingleton<IRecurringScheduleManager>(serviceProvider => serviceProvider.GetRequiredService<StubScheduleInspectionReader>());
        builder.Services.AddSingleton<IConcurrencyGroupInspectionReader, StubConcurrencyGroupInspectionReader>();
        builder.Services.AddSingleton<INodeInspectionReader, StubNodeInspectionReader>();
        builder.Services.AddSingleton<IMetricsInspectionReader, StubMetricsInspectionReader>();
        builder.Services.AddSingleton<IDashboardThroughputReader, StubDashboardThroughputReader>();
        builder.Services.AddShedduellerDashboard(options =>
        {
            options.Prerender = prerender;
            configureDashboard?.Invoke(options);
        });

        var app = builder.Build();
        if (mapWithWebApplication)
        {
            app.MapShedduellerDashboard("/sheddueller");
        }
        else
        {
            ((IApplicationBuilder)app).MapShedduellerDashboard("/sheddueller");
        }

        await app.StartAsync();

        return app;
    }

    private static async Task<string> GetOkHtmlAsync(
        WebApplication app,
        string path)
    {
        var response = await app.GetTestClient().GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertShellRefresh(string html)
    {
        html.ShouldContain("Live Refresh");
        html.ShouldContain("sd-live-status");
        html.ShouldContain("Updated ");
        html.ShouldContain("Auto-refresh On");
    }

    private static void AssertAppearsBefore(
        string html,
        string earlier,
        string later)
    {
        var earlierIndex = html.IndexOf(earlier, StringComparison.Ordinal);
        var laterIndex = html.IndexOf(later, StringComparison.Ordinal);

        earlierIndex.ShouldBeGreaterThanOrEqualTo(0);
        laterIndex.ShouldBeGreaterThanOrEqualTo(0);
        earlierIndex.ShouldBeLessThan(laterIndex);
    }

    private static void AssertStatusCheckbox(
        string html,
        string state,
        bool isChecked)
    {
        var markerIndex = html.IndexOf(string.Create(CultureInfo.InvariantCulture, $"<span>{state}</span>"), StringComparison.Ordinal);
        markerIndex.ShouldBeGreaterThanOrEqualTo(0);

        var startIndex = html.LastIndexOf("<label", markerIndex, StringComparison.Ordinal);
        var endIndex = html.IndexOf("</label>", markerIndex, StringComparison.Ordinal);

        startIndex.ShouldBeGreaterThanOrEqualTo(0);
        endIndex.ShouldBeGreaterThan(startIndex);

        var label = html[startIndex..(endIndex + "</label>".Length)];
        if (isChecked)
        {
            label.ShouldContain("checked");
            return;
        }

        label.ShouldNotContain("checked");
    }

    private static void AssertSelectValue(
        string html,
        string ariaLabel,
        string value)
    {
        var markerIndex = html.IndexOf(string.Create(CultureInfo.InvariantCulture, $"aria-label=\"{ariaLabel}\""), StringComparison.Ordinal);
        markerIndex.ShouldBeGreaterThanOrEqualTo(0);

        var startIndex = html.LastIndexOf("<select", markerIndex, StringComparison.Ordinal);
        var endIndex = html.IndexOf("</select>", markerIndex, StringComparison.Ordinal);

        startIndex.ShouldBeGreaterThanOrEqualTo(0);
        endIndex.ShouldBeGreaterThan(startIndex);

        var select = html[startIndex..(endIndex + "</select>".Length)];
        var selectOpen = select[..select.IndexOf('>', StringComparison.Ordinal)];
        if (selectOpen.Contains(string.Create(CultureInfo.InvariantCulture, $"value=\"{value}\""), StringComparison.Ordinal))
        {
            return;
        }

        var optionMarker = string.Create(CultureInfo.InvariantCulture, $"value=\"{value}\"");
        var optionMarkerIndex = select.IndexOf(optionMarker, StringComparison.Ordinal);
        optionMarkerIndex.ShouldBeGreaterThanOrEqualTo(0);
        var optionStartIndex = select.LastIndexOf("<option", optionMarkerIndex, StringComparison.Ordinal);
        var optionEndIndex = select.IndexOf("</option>", optionMarkerIndex, StringComparison.Ordinal);
        optionStartIndex.ShouldBeGreaterThanOrEqualTo(0);
        optionEndIndex.ShouldBeGreaterThan(optionStartIndex);
        var option = select[optionStartIndex..(optionEndIndex + "</option>".Length)];
        option.ShouldContain("selected");
    }

    private static void AssertCancelButton(
        string html,
        string expectedText,
        string expectedTitle,
        bool disabled)
    {
        var button = GetCancelButtonHtml(html);
        button.ShouldContain(expectedText);
        button.ShouldContain(expectedTitle);

        if (disabled)
        {
            button.ShouldContain("disabled");
            return;
        }

        button.ShouldNotContain("disabled");
    }

    private static string GetCancelButtonHtml(string html)
    {
        var markerIndex = html.IndexOf("job-detail-cancel-action", StringComparison.Ordinal);
        markerIndex.ShouldBeGreaterThanOrEqualTo(0);

        var startIndex = html.LastIndexOf("<button", markerIndex, StringComparison.Ordinal);
        var endIndex = html.IndexOf("</button>", markerIndex, StringComparison.Ordinal);

        startIndex.ShouldBeGreaterThanOrEqualTo(0);
        endIndex.ShouldBeGreaterThan(startIndex);

        return html[startIndex..(endIndex + "</button>".Length)];
    }

    private sealed class StubJobInspectionReader : IJobInspectionReader
    {
        public static readonly Guid JobId = Guid.Parse("8c32d457-9e7a-42bb-8947-0c8fa54743be");
        public static readonly Guid QueuedJobId = Guid.Parse("55c3d376-0b60-4564-a0ad-f65dd256a7a1");
        public static readonly Guid CancellationRequestedJobId = Guid.Parse("97c7be65-f241-4e70-bafe-e9df8bed53ee");
        public static readonly Guid CompletedJobId = Guid.Parse("a1543a1d-b7e0-4b43-b7ed-62249dc117be");
        public static readonly Guid FailedJobId = Guid.Parse("b4f8131d-a097-4410-8f47-8d37387e1357");
        public static readonly Guid CanceledJobId = Guid.Parse("d271e83d-5657-4a4f-9644-dd3b07911efe");
        public static readonly Guid MissingJobId = Guid.Parse("ec6b4cc0-5f54-4c9a-b9bf-06e3454a16d0");
        private static readonly DateTimeOffset EnqueuedAtUtc = DateTimeOffset.Parse("2026-04-20T12:00:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset FirstClaimedAtUtc = DateTimeOffset.Parse("2026-04-20T12:01:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset FirstFailedAtUtc = DateTimeOffset.Parse("2026-04-20T12:04:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset ClaimedAtUtc = DateTimeOffset.Parse("2026-04-20T12:05:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset ProgressAtUtc = DateTimeOffset.Parse("2026-04-20T12:08:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset CancellationRequestedAtUtc = DateTimeOffset.Parse("2026-04-20T12:08:30Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-04-20T12:09:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset CanceledAtUtc = DateTimeOffset.Parse("2026-04-20T12:02:00Z", CultureInfo.InvariantCulture);
        private static readonly DateTimeOffset LeaseExpiresAtUtc = DateTimeOffset.Parse("2026-04-20T12:10:00Z", CultureInfo.InvariantCulture);
        private static readonly JobInvocationInspection Invocation = new(
          JobInvocationTargetKind.Instance,
          "Sheddueller.Dashboard.Tests.DashboardEndpointTests.StubService",
          "Run",
          string.Join(
            Environment.NewLine,
            "StubService.Run(",
            "    \"permanent-failure\",",
            "    Job.Resolve<StubDependency>(),",
            "    Job.Context,",
            "    CancellationToken)"),
          [
              new JobInvocationParameterInspection(
                0,
                typeof(string).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
                "\"permanent-failure\""),
              new JobInvocationParameterInspection(
                1,
                "Sheddueller.Dashboard.Tests.DashboardEndpointTests.StubDependency",
                new JobMethodParameterBinding(JobMethodParameterBindingKind.Service, "Sheddueller.Dashboard.Tests.DashboardEndpointTests.StubDependency")),
              new JobInvocationParameterInspection(
                2,
                typeof(IJobContext).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.JobContext)),
              new JobInvocationParameterInspection(
                3,
                typeof(CancellationToken).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken)),
          ],
          SystemTextJsonJobPayloadSerializer.JsonContentType,
          SerializedArgumentsByteCount: 31,
          JobSerializedArgumentsInspectionStatus.Displayable);

        private static readonly JobInspectionSummary Job = new(
          JobId,
          JobState.Claimed,
          "Sheddueller.Dashboard.Tests.DashboardEndpointTests.StubService",
          "Run",
          Priority: 10,
          EnqueueSequence: 1,
          EnqueuedAtUtc,
          NotBeforeUtc: null,
          AttemptCount: 2,
          MaxAttempts: 5,
          Tags:
          [
              new JobTag("tenant", "acme"),
              new JobTag("schedule", "daily_rollup"),
              new JobTag("domain", "payments"),
              new JobTag("source", "stub"),
          ],
          ConcurrencyGroupKeys: ["tenant-acme", "daily-rollup"],
          SourceScheduleKey: "daily_rollup",
          LatestProgress: new JobProgressSnapshot(45, "Processing records chunk 4 of 9", ProgressAtUtc),
          new JobQueuePosition(JobId, JobQueuePositionKind.Claimed, Position: null, "Job is currently claimed."),
          ClaimedAtUtc,
          CompletedAtUtc: null,
          FailedAtUtc: null,
          CanceledAtUtc: null);

        private static readonly JobInspectionSummary QueuedJob = Job with
        {
            JobId = QueuedJobId,
            State = JobState.Queued,
            AttemptCount = 0,
            LatestProgress = null,
            QueuePosition = new JobQueuePosition(QueuedJobId, JobQueuePositionKind.Claimable, Position: 1, "Job is next in queue."),
        };

        private static readonly JobInspectionSummary CancellationRequestedJob = Job with
        {
            JobId = CancellationRequestedJobId,
            CancellationRequestedAtUtc = CancellationRequestedAtUtc,
        };

        private static readonly JobInspectionSummary CompletedJob = Job with
        {
            JobId = CompletedJobId,
            State = JobState.Completed,
            LatestProgress = null,
            QueuePosition = new JobQueuePosition(CompletedJobId, JobQueuePositionKind.Terminal, Position: null, "Job is terminal."),
            CompletedAtUtc = CompletedAtUtc,
        };

        private static readonly JobInspectionSummary FailedJob = Job with
        {
            JobId = FailedJobId,
            State = JobState.Failed,
            QueuePosition = new JobQueuePosition(FailedJobId, JobQueuePositionKind.Terminal, Position: null, "Job is terminal."),
            FailedAtUtc = FirstFailedAtUtc,
        };

        private static readonly JobInspectionSummary CanceledJob = Job with
        {
            JobId = CanceledJobId,
            State = JobState.Canceled,
            LatestProgress = null,
            QueuePosition = new JobQueuePosition(CanceledJobId, JobQueuePositionKind.Canceled, Position: null, "Job is canceled."),
            CanceledAtUtc = CanceledAtUtc,
        };

        private static readonly IReadOnlyList<JobInspectionSummary> DetailJobs =
        [
            Job,
            QueuedJob,
            CancellationRequestedJob,
            CompletedJob,
            FailedJob,
            CanceledJob,
        ];

        private static readonly IReadOnlyList<JobEvent> Events =
        [
            new(
              Guid.Parse("74b22b3d-446b-4960-a465-46f60edc91d1"),
              JobId,
              EventSequence: 1,
              JobEventKind.Lifecycle,
              EnqueuedAtUtc,
              AttemptNumber: 0,
              Message: "Queued"),
            new(
              Guid.Parse("f7272682-4c54-4490-9a62-c2897b391330"),
              JobId,
              EventSequence: 2,
              JobEventKind.AttemptStarted,
              FirstClaimedAtUtc,
              AttemptNumber: 1,
              Message: "Claimed by worker-us-east-1b-02"),
            new(
              Guid.Parse("8023b28d-148d-4702-96a9-7ced0a6495c2"),
              JobId,
              EventSequence: 3,
              JobEventKind.AttemptFailed,
              FirstFailedAtUtc,
              AttemptNumber: 1,
              Message: "ConnectionTimeoutError: db-primary cluster unreachable"),
            new(
              Guid.Parse("694b0bd9-4bb3-4278-b884-435860e5c9da"),
              JobId,
              EventSequence: 4,
              JobEventKind.AttemptStarted,
              ClaimedAtUtc,
              AttemptNumber: 2,
              Message: "Claimed by worker-us-east-1a-04"),
            new(
              Guid.Parse("6fc26ee9-1fc7-4b0d-9f19-7907dd44597c"),
              JobId,
              EventSequence: 5,
              JobEventKind.Log,
              ClaimedAtUtc.AddSeconds(1),
              AttemptNumber: 2,
              LogLevel: JobLogLevel.Information,
              Message: "Initializing worker environment..."),
            new(
              Guid.Parse("80b49674-a74a-440f-9bea-01a917e8a477"),
              JobId,
              EventSequence: 6,
              JobEventKind.Progress,
              ProgressAtUtc,
              AttemptNumber: 2,
              Message: "progress event row should be hidden",
              ProgressPercent: 45),
        ];

        private static readonly JobInspectionOverview Overview = new(
          new Dictionary<JobState, int> { [JobState.Claimed] = 1, [JobState.Failed] = 1 },
          [Job],
          [FailedJob],
          [],
          [],
          []);

        public ValueTask<JobInspectionOverview> GetOverviewAsync(
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(Overview);

        public ValueTask<JobInspectionPage> SearchJobsAsync(
            JobInspectionQuery query,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(new JobInspectionPage([Job, QueuedJob], ContinuationToken: null, TotalCount: 2));

        public ValueTask<JobInspectionDetail?> GetJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var job = DetailJobs.FirstOrDefault(job => job.JobId == jobId);
            return ValueTask.FromResult(job is null ? null : CreateDetail(job));
        }

        public ValueTask<JobQueuePosition> GetQueuePositionAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(new JobQueuePosition(jobId, JobQueuePositionKind.NotFound, Position: null, "Job was not found."));

        public IAsyncEnumerable<JobEvent> ReadEventsAsync(
            Guid jobId,
            JobEventReadOptions? options = null,
            CancellationToken cancellationToken = default)
          => ReadEventsCoreAsync(jobId, options, cancellationToken);

        private static async IAsyncEnumerable<JobEvent> ReadEventsCoreAsync(
            Guid jobId,
            JobEventReadOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            options ??= new JobEventReadOptions();

            foreach (var jobEvent in Events
              .Where(jobEvent => jobEvent.JobId == jobId)
              .Where(jobEvent => options.AfterEventSequence is null || jobEvent.EventSequence > options.AfterEventSequence.Value)
              .Take(options.Limit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return jobEvent;
            }
        }

        private static JobInspectionDetail CreateDetail(JobInspectionSummary job)
          => (job.State == JobState.Queued
            ? new JobInspectionDetail(
              job,
              ClaimedAtUtc: null,
              ClaimedByNodeId: null,
              LeaseExpiresAtUtc: null,
              ScheduledFireAtUtc: null)
            : new JobInspectionDetail(
              job,
              ClaimedAtUtc,
              ClaimedByNodeId: "worker-us-east-1a-04",
              job.State == JobState.Claimed ? LeaseExpiresAtUtc : null,
              ScheduledFireAtUtc: null)) with
          {
              Invocation = Invocation,
          };
    }

    private sealed class StubJobManager : IJobManager
    {
        public ValueTask<JobCancellationResult> CancelAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (jobId == StubJobInspectionReader.QueuedJobId)
            {
                return ValueTask.FromResult(JobCancellationResult.Canceled);
            }

            if (jobId == StubJobInspectionReader.JobId
              || jobId == StubJobInspectionReader.CancellationRequestedJobId)
            {
                return ValueTask.FromResult(JobCancellationResult.CancellationRequested);
            }

            if (jobId == StubJobInspectionReader.CompletedJobId
              || jobId == StubJobInspectionReader.FailedJobId
              || jobId == StubJobInspectionReader.CanceledJobId)
            {
                return ValueTask.FromResult(JobCancellationResult.AlreadyFinished);
            }

            return ValueTask.FromResult(JobCancellationResult.NotFound);
        }
    }

    private sealed class StubScheduleInspectionReader : IScheduleInspectionReader, IRecurringScheduleManager
    {
        private static readonly DateTimeOffset UpdatedAtUtc = DateTimeOffset.Parse("2026-04-20T12:03:00Z", CultureInfo.InvariantCulture);

        private static readonly IReadOnlyList<ScheduleInspectionSummary> Schedules =
        [
            new(
              "etl_nightly_sync",
              "Sheddueller.Dashboard.Tests.Schedules.EtlSyncWorker",
              "Run",
              "0 2 * * *",
              IsPaused: false,
              RecurringOverlapMode.Skip,
              DateTimeOffset.Parse("2026-04-21T02:00:00Z", CultureInfo.InvariantCulture),
              Priority: 10,
              ConcurrencyGroupKeys: ["etl-nodes"],
              Tags:
              [
                  new JobTag("tenant", "acme"),
                  new JobTag("schedule", "nightly"),
              ],
              UpdatedAtUtc),
            new(
              "cache_eviction_hourly",
              "Sheddueller.Dashboard.Tests.Schedules.CacheCleaner",
              "Clean",
              "0 * * * *",
              IsPaused: true,
              RecurringOverlapMode.Allow,
              NextFireAtUtc: null,
              Priority: 2,
              ConcurrencyGroupKeys: ["maintenance"],
              Tags:
              [
                  new JobTag("area", "cache"),
              ],
              UpdatedAtUtc.AddMinutes(-45)),
            new(
              "billing_reconciliation",
              "Sheddueller.Dashboard.Tests.Schedules.BillingReconciler",
              "Reconcile",
              "0 0 1 * *",
              IsPaused: false,
              RecurringOverlapMode.Skip,
              DateTimeOffset.Parse("2026-05-01T00:00:00Z", CultureInfo.InvariantCulture),
              Priority: 100,
              ConcurrencyGroupKeys: ["finance-secure"],
              Tags:
              [
                  new JobTag("tier", "p0"),
                  new JobTag("domain", "finance"),
              ],
              UpdatedAtUtc.AddDays(-1)),
        ];

        public ValueTask<ScheduleInspectionPage> SearchSchedulesAsync(
            ScheduleInspectionQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filtered = Schedules
              .Where(schedule => string.IsNullOrWhiteSpace(query.ScheduleKey) || schedule.ScheduleKey.Contains(query.ScheduleKey.Trim(), StringComparison.OrdinalIgnoreCase))
              .Where(schedule => query.IsPaused is null || schedule.IsPaused == query.IsPaused.Value)
              .Where(schedule => query.ServiceType is null || string.Equals(schedule.ServiceType, query.ServiceType, StringComparison.Ordinal))
              .Where(schedule => query.MethodName is null || string.Equals(schedule.MethodName, query.MethodName, StringComparison.Ordinal))
              .Where(schedule => query.Tag is null || schedule.Tags.Contains(query.Tag, EqualityComparer<JobTag>.Default))
              .ToArray();
            var schedules = filtered
              .Where(schedule => query.ContinuationToken is null || string.Compare(schedule.ScheduleKey, query.ContinuationToken, StringComparison.Ordinal) > 0)
              .Take(query.PageSize)
              .ToArray();

            var hasFilters = query.ScheduleKey is not null
              || query.IsPaused is not null
              || query.ServiceType is not null
              || query.MethodName is not null
              || query.Tag is not null;
            var continuationToken = !hasFilters && query.ContinuationToken is null ? "billing_reconciliation" : null;

            return ValueTask.FromResult(new ScheduleInspectionPage(schedules, continuationToken, filtered.LongLength));
        }

        public ValueTask<ScheduleInspectionDetail?> GetScheduleAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<ScheduleInspectionDetail?>(null);

        public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
            string scheduleKey,
            string cronExpression,
            Expression<Func<TService, CancellationToken, Task>> work,
            RecurringScheduleOptions? options = null,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
            string scheduleKey,
            string cronExpression,
            Expression<Func<TService, CancellationToken, ValueTask>> work,
            RecurringScheduleOptions? options = null,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
            string scheduleKey,
            string cronExpression,
            Expression<Func<CancellationToken, Task>> work,
            RecurringScheduleOptions? options = null,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
            string scheduleKey,
            string cronExpression,
            Expression<Func<CancellationToken, ValueTask>> work,
            RecurringScheduleOptions? options = null,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleTriggerResult> TriggerAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(string.Equals(scheduleKey, "etl_nightly_sync", StringComparison.Ordinal)
              ? new RecurringScheduleTriggerResult(
                RecurringScheduleTriggerStatus.Enqueued,
                Guid.Parse("5a8f55df-9d29-47c1-8510-cebe102502bf"),
                EnqueueSequence: 42)
              : new RecurringScheduleTriggerResult(RecurringScheduleTriggerStatus.NotFound));
        }

        public ValueTask<bool> PauseAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(true);

        public ValueTask<bool> ResumeAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(true);

        public ValueTask<RecurringScheduleInfo?> GetAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<RecurringScheduleInfo?>(null);

        public async IAsyncEnumerable<RecurringScheduleInfo> ListAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubConcurrencyGroupInspectionReader : IConcurrencyGroupInspectionReader
    {
        private static readonly DateTimeOffset UpdatedAtUtc = DateTimeOffset.Parse("2026-04-20T12:02:01Z", CultureInfo.InvariantCulture);

        private static readonly IReadOnlyList<ConcurrencyGroupInspectionSummary> Groups =
        [
            new(
              "pool_etl_heavy",
              EffectiveLimit: 50,
              CurrentOccupancy: 50,
              BlockedJobCount: 12,
              IsSaturated: true,
              UpdatedAtUtc),
            new(
              "api_sync_workers",
              EffectiveLimit: 100,
              CurrentOccupancy: 85,
              BlockedJobCount: 0,
              IsSaturated: false,
              UpdatedAtUtc.AddMinutes(-1)),
            new(
              "bg_maintenance",
              EffectiveLimit: 10,
              CurrentOccupancy: 2,
              BlockedJobCount: 0,
              IsSaturated: false,
              UpdatedAtUtc.AddMinutes(-5)),
            new(
              "db_vacuum_ops",
              EffectiveLimit: 5,
              CurrentOccupancy: 2,
              BlockedJobCount: 1,
              IsSaturated: false,
              UpdatedAtUtc.AddMinutes(-12)),
        ];

        public ValueTask<ConcurrencyGroupInspectionPage> SearchConcurrencyGroupsAsync(
            ConcurrencyGroupInspectionQuery query,
            CancellationToken cancellationToken = default)
        {
            var filtered = Groups
              .Where(group => query.GroupKey is null || string.Equals(group.GroupKey, query.GroupKey, StringComparison.Ordinal))
              .Where(group => query.IsSaturated is null || group.IsSaturated == query.IsSaturated.Value)
              .Where(group => query.HasBlockedJobs is null || (group.BlockedJobCount > 0) == query.HasBlockedJobs.Value)
              .ToArray();
            var groups = filtered
              .Where(group => query.ContinuationToken is null || string.Compare(group.GroupKey, query.ContinuationToken, StringComparison.Ordinal) > 0)
              .Take(query.PageSize)
              .ToArray();

            return ValueTask.FromResult(new ConcurrencyGroupInspectionPage(
              groups,
              query.IsSaturated is null && query.HasBlockedJobs is null && query.ContinuationToken is null ? "db_vacuum_ops" : null,
              filtered.LongLength));
        }

        public ValueTask<ConcurrencyGroupInspectionDetail?> GetConcurrencyGroupAsync(
            string groupKey,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<ConcurrencyGroupInspectionDetail?>(null);
    }

    private sealed class StubNodeInspectionReader : INodeInspectionReader
    {
        private static readonly DateTimeOffset FirstSeenAtUtc = DateTimeOffset.Parse("2026-04-20T12:00:00Z", CultureInfo.InvariantCulture);

        private static readonly IReadOnlyList<NodeInspectionSummary> Nodes =
        [
            new(
              "wrk-prod-us-east-1a-8f92",
              NodeHealthState.Active,
              FirstSeenAtUtc,
              DateTimeOffset.Parse("2026-04-20T12:04:30Z", CultureInfo.InvariantCulture),
              ClaimedJobCount: 14205,
              MaxConcurrentExecutionsPerNode: 4,
              CurrentExecutionCount: 3),
            new(
              "wrk-prod-eu-west-1a-7b22",
              NodeHealthState.Stale,
              FirstSeenAtUtc.AddMinutes(1),
              DateTimeOffset.Parse("2026-04-20T12:03:20Z", CultureInfo.InvariantCulture),
              ClaimedJobCount: 4102,
              MaxConcurrentExecutionsPerNode: 4,
              CurrentExecutionCount: 1),
            new(
              "wrk-prod-ap-south-1b-1f00",
              NodeHealthState.Dead,
              FirstSeenAtUtc.AddMinutes(2),
              DateTimeOffset.Parse("2026-04-20T10:00:00Z", CultureInfo.InvariantCulture),
              ClaimedJobCount: 1024,
              MaxConcurrentExecutionsPerNode: 4,
              CurrentExecutionCount: 0),
        ];

        public ValueTask<NodeInspectionPage> SearchNodesAsync(
            NodeInspectionQuery query,
            CancellationToken cancellationToken = default)
        {
            var filtered = Nodes
              .Where(node => query.State is null || node.State == query.State.Value)
              .ToArray();
            var nodes = filtered
              .Where(node => query.ContinuationToken is null || string.Compare(node.NodeId, query.ContinuationToken, StringComparison.Ordinal) > 0)
              .Take(query.PageSize)
              .ToArray();

            return ValueTask.FromResult(new NodeInspectionPage(
              nodes,
              query.State is null && query.ContinuationToken is null ? "wrk-prod-ap-south-1b-1f00" : null,
              filtered.LongLength));
        }

        public ValueTask<NodeInspectionDetail?> GetNodeAsync(
            string nodeId,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<NodeInspectionDetail?>(null);
    }

    private sealed class StubMetricsInspectionReader : IMetricsInspectionReader
    {
        private static readonly MetricsInspectionSnapshot Snapshot = new(
        [
            new(
              TimeSpan.FromMinutes(5),
              QueuedCount: 2,
              ClaimedCount: 1,
              FailedCount: 0,
              CanceledCount: 0,
              OldestQueuedAge: TimeSpan.FromMinutes(1),
              EnqueueRatePerMinute: 0.2,
              ClaimRatePerMinute: 0.1,
              SuccessRatePerMinute: 0.1,
              FailureRatePerMinute: 0,
              CancellationRatePerMinute: 0,
              RetryRatePerMinute: 0,
              P50QueueLatency: TimeSpan.FromSeconds(1),
              P95QueueLatency: TimeSpan.FromSeconds(2),
              P50ExecutionDuration: TimeSpan.FromSeconds(3),
              P95ExecutionDuration: TimeSpan.FromSeconds(4),
              P95ScheduleFireLag: TimeSpan.FromSeconds(5),
              SaturatedConcurrencyGroupCount: 0,
              ActiveNodeCount: 2,
              StaleNodeCount: 1,
              DeadNodeCount: 1),
            new(
              TimeSpan.FromHours(1),
              QueuedCount: 1204,
              ClaimedCount: 84,
              FailedCount: 3,
              CanceledCount: 1,
              OldestQueuedAge: TimeSpan.FromSeconds(45),
              EnqueueRatePerMinute: 914.2,
              ClaimRatePerMinute: 870.5,
              SuccessRatePerMinute: 842.3,
              FailureRatePerMinute: 0.2,
              CancellationRatePerMinute: 0.1,
              RetryRatePerMinute: 0.6,
              P50QueueLatency: TimeSpan.FromMilliseconds(15),
              P95QueueLatency: TimeSpan.FromMilliseconds(120),
              P50ExecutionDuration: TimeSpan.FromMilliseconds(250),
              P95ExecutionDuration: TimeSpan.FromMilliseconds(1200),
              P95ScheduleFireLag: TimeSpan.FromMilliseconds(12),
              SaturatedConcurrencyGroupCount: 2,
              ActiveNodeCount: 24,
              StaleNodeCount: 1,
              DeadNodeCount: 0),
            new(
              TimeSpan.FromHours(24),
              QueuedCount: 48201,
              ClaimedCount: 213,
              FailedCount: 18,
              CanceledCount: 4,
              OldestQueuedAge: TimeSpan.FromMinutes(12),
              EnqueueRatePerMinute: 703.7,
              ClaimRatePerMinute: 699.4,
              SuccessRatePerMinute: 682.1,
              FailureRatePerMinute: 0.7,
              CancellationRatePerMinute: 0.2,
              RetryRatePerMinute: 3.4,
              P50QueueLatency: TimeSpan.FromMilliseconds(32),
              P95QueueLatency: TimeSpan.FromMilliseconds(410),
              P50ExecutionDuration: TimeSpan.FromMilliseconds(460),
              P95ExecutionDuration: TimeSpan.FromSeconds(3),
              P95ScheduleFireLag: TimeSpan.FromMilliseconds(95),
              SaturatedConcurrencyGroupCount: 4,
              ActiveNodeCount: 25,
              StaleNodeCount: 2,
              DeadNodeCount: 1),
        ]);

        public ValueTask<MetricsInspectionSnapshot> GetMetricsAsync(
            MetricsInspectionQuery query,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(Snapshot);
    }

    private sealed class StubDashboardThroughputReader : IDashboardThroughputReader
    {
        private static readonly DateTimeOffset WindowEndUtc = new(2026, 4, 20, 12, 30, 0, TimeSpan.Zero);

        private static readonly DashboardThroughputSnapshot Snapshot = new(
          WindowEndUtc.AddSeconds(-2),
          WindowEndUtc,
          TimeSpan.FromSeconds(1),
          [
              new DashboardThroughputBucket(
                WindowEndUtc.AddSeconds(-2),
                QueuedCount: 0,
                StartedCount: 0,
                SucceededCount: 0,
                FailedCount: 0,
                CanceledCount: 0,
                FailedAttemptCount: 0),
              new DashboardThroughputBucket(
                WindowEndUtc.AddSeconds(-1),
                QueuedCount: 12,
                StartedCount: 10,
                SucceededCount: 9,
                FailedCount: 1,
                CanceledCount: 0,
                FailedAttemptCount: 2),
              new DashboardThroughputBucket(
                WindowEndUtc,
                QueuedCount: 14,
                StartedCount: 11,
                SucceededCount: 8,
                FailedCount: 0,
                CanceledCount: 1,
                FailedAttemptCount: 3),
          ]);

        public DashboardThroughputSnapshot GetSnapshot()
          => Snapshot;
    }
}
