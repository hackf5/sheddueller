using Npgsql;

using Sheddueller;
using Sheddueller.Dashboard;
using Sheddueller.Postgres;
using Sheddueller.SampleHost;
using Sheddueller.SampleHost.DemoJobs;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Sheddueller")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Sheddueller' is required.");
var schemaName = builder.Configuration["Sheddueller:Postgres:SchemaName"] ?? "sheddueller";
await using var dataSource = NpgsqlDataSource.Create(connectionString);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<DemoJobState>();
builder.Services.AddTransient<DemoJobService>();

builder.Services.AddSheddueller(sheddueller => sheddueller
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

app.MapGet("/", async (
    HttpContext httpContext,
    IDashboardJobReader reader,
    IRecurringScheduleManager scheduleManager,
    CancellationToken cancellationToken) =>
{
    var statusMessage = httpContext.Request.Query["message"].ToString();
    var html = await LauncherPageRenderer.RenderAsync(statusMessage, reader, scheduleManager, cancellationToken).ConfigureAwait(false);
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

app.MapPost("/launch/cancel", async (HttpContext httpContext, IJobManager jobManager, CancellationToken cancellationToken) =>
{
    var form = await httpContext.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
    var value = form["jobId"].ToString();
    if (!Guid.TryParse(value, out var jobId))
    {
        return RedirectWithMessage($"'{value}' is not a valid job id.");
    }

    var canceled = await jobManager.CancelAsync(jobId, cancellationToken).ConfigureAwait(false);
    return RedirectWithMessage(canceled
      ? $"Canceled queued job {jobId:D}."
      : $"Job {jobId:D} was not queued, so it could not be canceled.");
});

((IApplicationBuilder)app).MapShedduellerDashboard("/sheddueller");

await app.RunAsync();

static IResult RedirectWithMessage(string message)
  => Results.Redirect($"/?message={Uri.EscapeDataString(message)}");
