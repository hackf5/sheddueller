using Npgsql;

using Sheddueller;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Postgres;
using Sheddueller.SampleHost;
using Sheddueller.SampleHost.DemoJobs;
using Sheddueller.Storage;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Sheddueller")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Sheddueller' is required.");
var schemaName = builder.Configuration["Sheddueller:Postgres:SchemaName"] ?? "sheddueller";
await using var dataSource = NpgsqlDataSource.Create(connectionString);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<DemoJobState>();
builder.Services.AddTransient<DemoJobService>();

builder.Services.AddShedduellerWorker(sheddueller => sheddueller
  .UsePostgres(options =>
  {
      options.DataSource = dataSource;
      options.SchemaName = schemaName;
  })
  .ConfigureOptions(options =>
  {
      options.NodeId = "sample-host";
      options.MaxConcurrentExecutionsPerNode = 4;
      options.IdlePollingInterval = TimeSpan.FromMilliseconds(250);
      options.LeaseDuration = TimeSpan.FromSeconds(20);
      options.HeartbeatInterval = TimeSpan.FromSeconds(5);
      options.DefaultRetryPolicy = new RetryPolicy(3, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2));
  }));
builder.Services.AddShedduellerDashboard(options => options.EventRetention = TimeSpan.FromDays(14));

var app = builder.Build();

await app.Services.GetRequiredService<IPostgresMigrator>().ApplyAsync();

app.UseAntiforgery();

app.MapGet("/", (HttpContext httpContext) =>
{
    var statusMessage = httpContext.Request.Query["message"].ToString();
    var html = LauncherPageRenderer.Render(statusMessage);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/launch/quick-success", async (IJobEnqueuer enqueuer, CancellationToken cancellationToken) =>
{
    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunQuickAsync("quick-success", ct),
      cancellationToken: cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Queued quick success job {jobId:D}.");
});

app.MapPost("/launch/progress", async (IJobEnqueuer enqueuer, CancellationToken cancellationToken) =>
{
    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunProgressAsync("progress-demo", Job.Context, ct),
      cancellationToken: cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Queued progress demo job {jobId:D}.");
});

app.MapPost("/launch/retry-then-succeed", async (IJobEnqueuer enqueuer, CancellationToken cancellationToken) =>
{
    var runKey = $"retry-demo:{Guid.NewGuid():N}";
    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunRetryUntilSuccessAsync(runKey, 2, Job.Context, ct),
      new JobSubmission(RetryPolicy: new RetryPolicy(4, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2))),
      cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Queued retry demo job {jobId:D}. It will fail twice before succeeding.");
});

app.MapPost("/launch/permanent-failure", async (IJobEnqueuer enqueuer, CancellationToken cancellationToken) =>
{
    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunAlwaysFailAsync("permanent-failure", Job.Context, ct),
      new JobSubmission(RetryPolicy: new RetryPolicy(1, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(1))),
      cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Queued permanent failure job {jobId:D}.");
});

app.MapPost("/launch/delayed", async (IJobEnqueuer enqueuer, CancellationToken cancellationToken) =>
{
    var notBeforeUtc = DateTimeOffset.UtcNow.AddSeconds(30);
    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunQuickAsync("delayed-demo", ct),
      new JobSubmission(NotBeforeUtc: notBeforeUtc),
      cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Queued delayed job {jobId:D} for {notBeforeUtc:O}.");
});

app.MapPost("/launch/blocking-batch", async (
    IConcurrencyGroupManager concurrencyGroupManager,
    IJobEnqueuer enqueuer,
    CancellationToken cancellationToken) =>
{
    const string GroupKey = "demo:blocking";
    await concurrencyGroupManager.SetLimitAsync(GroupKey, 1, cancellationToken).ConfigureAwait(false);

    var jobIds = new List<Guid>();
    for (var index = 1; index <= 4; index++)
    {
        var label = $"blocking-{index}";
        var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
          (service, ct) => service.RunGroupHoldAsync(label, Job.Context, ct),
          new JobSubmission(Priority: 25, ConcurrencyGroupKeys: [GroupKey]),
          cancellationToken).ConfigureAwait(false);
        jobIds.Add(jobId);
    }

    return RedirectWithMessage($"Queued {jobIds.Count} concurrency-demo jobs in group '{GroupKey}' with limit 1.");
});

app.MapPost("/launch/idempotent", async (
    IConcurrencyGroupManager concurrencyGroupManager,
    IJobEnqueuer enqueuer,
    IJobInspectionReader inspectionReader,
    CancellationToken cancellationToken) =>
{
    const string GroupKey = "demo:idempotent-reprice";
    const string WorkLabel = "reprice-listing-3";

    await concurrencyGroupManager.SetLimitAsync(GroupKey, 1, cancellationToken).ConfigureAwait(false);

    if (!await HasNonTerminalJobsInGroupAsync(inspectionReader, GroupKey, cancellationToken).ConfigureAwait(false))
    {
        _ = await enqueuer.EnqueueAsync<DemoJobService>(
          (service, ct) => service.RunIdempotentDemoAsync("idempotent-demo-slot-holder", Job.Context, ct),
          new JobSubmission(
            Priority: 50,
            ConcurrencyGroupKeys: [GroupKey],
            Tags: [new JobTag("demo", "idempotent-slot-holder")]),
          cancellationToken).ConfigureAwait(false);
    }

    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunIdempotentDemoAsync(WorkLabel, Job.Context, ct),
      new JobSubmission(
        Priority: 25,
        ConcurrencyGroupKeys: [GroupKey],
        Tags: [new JobTag("demo", "idempotent"), new JobTag("listing", "3")],
        IdempotencyKind: JobIdempotencyKind.MethodAndArguments),
      cancellationToken).ConfigureAwait(false);

    return RedirectWithMessage($"Queued idempotent reprice job {jobId:D}. Click again quickly; the queued job id should be reused.");
});

app.MapPost("/launch/recurring", async (IRecurringScheduleManager scheduleManager, CancellationToken cancellationToken) =>
{
    var result = await scheduleManager.CreateOrUpdateAsync<DemoJobService>(
      "demo:recurring",
      "* * * * *",
      (service, ct) => service.RunRecurringAsync(Job.Context, ct),
      new RecurringScheduleOptions(Priority: 10, OverlapMode: RecurringOverlapMode.Skip),
      cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Recurring schedule 'demo:recurring' is {result}. The next occurrence will fire on the next minute boundary.");
});

app.MapPost("/launch/cancelable", async (IJobEnqueuer enqueuer, CancellationToken cancellationToken) =>
{
    var notBeforeUtc = DateTimeOffset.UtcNow.AddMinutes(2);
    var jobId = await enqueuer.EnqueueAsync<DemoJobService>(
      (service, ct) => service.RunQuickAsync("cancelable-delayed", ct),
      new JobSubmission(NotBeforeUtc: notBeforeUtc),
      cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage($"Queued cancelable delayed job {jobId:D} for {notBeforeUtc:O}.");
});

((IApplicationBuilder)app).MapShedduellerDashboard("/sheddueller");

await app.RunAsync();

static IResult RedirectWithMessage(string message)
  => Results.Redirect($"/?message={Uri.EscapeDataString(message)}");

static async ValueTask<bool> HasNonTerminalJobsInGroupAsync(
    IJobInspectionReader inspectionReader,
    string groupKey,
    CancellationToken cancellationToken)
{
    var page = await inspectionReader.SearchJobsAsync(
      new JobInspectionQuery(
        States: [JobState.Queued, JobState.Claimed],
        ConcurrencyGroupContains: groupKey,
        PageSize: 100),
      cancellationToken).ConfigureAwait(false);

    return page.Jobs.Any(job => job.ConcurrencyGroupKeys.Contains(groupKey, StringComparer.Ordinal));
}
