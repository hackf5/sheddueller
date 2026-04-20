namespace Sheddueller.Tests;

using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheddueller.Dashboard;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class WorkerTests
{
    [Fact]
    public async Task HostedWorker_QueuedJobs_ExecutesFromFreshScopesAndCompletes()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 13, 0, 0, TimeSpan.Zero);
        using var host = CreateHost(new ManualTimeProvider(timestamp));
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<IJobEnqueuer>();
        var store = host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        var first = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordAsync("first", cancellationToken));
        var second = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordAsync("second", cancellationToken));

        await WaitUntilAsync(() =>
          store.GetSnapshot(first)?.State == JobState.Completed
          && store.GetSnapshot(second)?.State == JobState.Completed);

        recorder.Values.Order(StringComparer.Ordinal).ShouldBe(["first", "second"]);
        recorder.ScopeIds.Distinct().Count().ShouldBe(2);
        store.GetSnapshot(first).ShouldNotBeNull().CompletedAtUtc.ShouldBe(timestamp);
        store.GetSnapshot(second).ShouldNotBeNull().CompletedAtUtc.ShouldBe(timestamp);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostedWorker_JobContextAwareJob_InjectsContextAndRecordsTelemetry()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<IJobEnqueuer>();
        var store = host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        var jobId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordWithContextAsync("context", Job.Context, cancellationToken));

        await WaitUntilAsync(() => store.GetSnapshot(jobId)?.State == JobState.Completed);

        recorder.Values.ShouldContain("context");
        recorder.ContextJobIds.ShouldContain(jobId);
        var events = new List<DashboardJobEvent>();
        await foreach (var jobEvent in store.ReadEventsAsync(jobId))
        {
            events.Add(jobEvent);
        }

        events.ShouldContain(jobEvent => jobEvent.Kind == DashboardJobEventKind.Log && jobEvent.Message == "context log");
        events.ShouldContain(jobEvent => jobEvent.Kind == DashboardJobEventKind.Progress && jobEvent.ProgressPercent == 25);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostedWorker_ContextAwareRecurringSchedule_InjectsContextIntoMaterializedJob()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 13, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(timestamp);
        using var host = CreateHost(timeProvider);
        await host.StartAsync();
        var scheduleManager = host.Services.GetRequiredService<IRecurringScheduleManager>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        await scheduleManager.CreateOrUpdateAsync<WorkerTestService>(
          "recurring-context",
          "* * * * *",
          (service, cancellationToken) => service.RecordWithContextAsync("recurring-context", Job.Context, cancellationToken));

        timeProvider.SetUtcNow(timestamp.AddMinutes(1));

        await WaitUntilAsync(() => recorder.Values.Contains("recurring-context")
          && recorder.ContextJobIds.Any(jobId => jobId != Guid.Empty));

        await host.StopAsync();
    }

    [Fact]
    public async Task HostedWorker_ThrowingJob_RecordsFailureDetails()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<IJobEnqueuer>();
        var store = host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();

        var jobId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.ThrowAsync("expected failure", cancellationToken));

        await WaitUntilAsync(() => store.GetSnapshot(jobId)?.State == JobState.Failed);

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.Failure.ShouldNotBeNull().ExceptionType.ShouldContain(nameof(InvalidOperationException));
        snapshot.Failure.Message.ShouldContain("expected failure");

        await host.StopAsync();
    }

    [Fact]
    public async Task HostedWorker_DirectStoreJob_ClaimsThroughFallbackPolling()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var store = host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        var serializer = host.Services.GetRequiredService<IJobPayloadSerializer>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();
        var jobId = Guid.NewGuid();
        var payload = await serializer.SerializeAsync(new object?[] { "direct" }, [typeof(string)]);

        await store.EnqueueAsync(new EnqueueJobRequest(
          jobId,
          0,
          typeof(WorkerTestService).AssemblyQualifiedName!,
          nameof(WorkerTestService.RecordAsync),
          [typeof(string).AssemblyQualifiedName!, typeof(CancellationToken).AssemblyQualifiedName!],
          payload,
          [],
          DateTimeOffset.UtcNow));

        await WaitUntilAsync(() => store.GetSnapshot(jobId)?.State == JobState.Completed);

        recorder.Values.ShouldContain("direct");

        await host.StopAsync();
    }

    [Fact]
    public async Task HostShutdown_CooperativeCancellation_RequeuesWithoutRetryBudgetConsumption()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<IJobEnqueuer>();
        var store = host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        var jobId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.WaitForShutdownAsync(cancellationToken));

        await recorder.WaitForShutdownTaskStartAsync();
        await host.StopAsync();

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.State.ShouldBe(JobState.Queued);
        snapshot.AttemptCount.ShouldBe(0);
        snapshot.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task HostedWorker_DueRecurringSchedule_MaterializesAndExecutesOccurrence()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 13, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(timestamp);
        using var host = CreateHost(timeProvider);
        await host.StartAsync();
        var scheduleManager = host.Services.GetRequiredService<IRecurringScheduleManager>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        await scheduleManager.CreateOrUpdateAsync<WorkerTestService>(
          "recurring-test",
          "* * * * *",
          (service, cancellationToken) => service.RecordAsync("recurring", cancellationToken));

        timeProvider.SetUtcNow(timestamp.AddMinutes(1));

        await WaitUntilAsync(() => recorder.Values.Contains("recurring"));

        await host.StopAsync();
    }

    [Fact]
    public async Task JobManager_QueuedJob_CancelsTask()
    {
        using var host = CreateHost();
        var enqueuer = host.Services.GetRequiredService<IJobEnqueuer>();
        var taskManager = host.Services.GetRequiredService<IJobManager>();
        var store = host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();

        var jobId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordAsync("cancel-me", cancellationToken),
          new JobSubmission(NotBeforeUtc: DateTimeOffset.UtcNow.AddHours(1)));

        (await taskManager.CancelAsync(jobId)).ShouldBeTrue();
        store.GetSnapshot(jobId).ShouldNotBeNull().State.ShouldBe(JobState.Canceled);
    }

    [Fact]
    public async Task RecurringScheduleRegistration_MissingSchedulerToken_RejectsExpression()
    {
        using var host = CreateHost();
        var scheduleManager = host.Services.GetRequiredService<IRecurringScheduleManager>();

        await Should.ThrowAsync<ArgumentException>(() => scheduleManager
          .CreateOrUpdateAsync<WorkerTestService>(
            "invalid-recurring",
            "* * * * *",
            (service, _) => service.RecordAsync("invalid", CancellationToken.None))
          .AsTask());
    }

    private static IHost CreateHost(TimeProvider? timeProvider = null)
    {
        var builder = Host.CreateApplicationBuilder();

        if (timeProvider is not null)
        {
            builder.Services.AddSingleton(timeProvider);
        }

        builder.Services.AddSingleton<WorkerExecutionRecorder>();
        builder.Services.AddScoped<WorkerScopeMarker>();
        builder.Services.AddTransient<WorkerTestService>();
        builder.Services.AddSheddueller(sheddueller => sheddueller
          .UseInMemoryStore()
          .ConfigureOptions(options =>
          {
              options.MaxConcurrentExecutionsPerNode = 2;
              options.IdlePollingInterval = TimeSpan.FromMilliseconds(10);
          }));

        return builder.Build();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class WorkerExecutionRecorder
    {
        private readonly TaskCompletionSource shutdownTaskStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<string> Values { get; } = new();

        public ConcurrentQueue<Guid> ScopeIds { get; } = new();

        public ConcurrentQueue<Guid> ContextJobIds { get; } = new();

        public void Record(Guid scopeId, string value)
        {
            Values.Enqueue(value);
            ScopeIds.Enqueue(scopeId);
        }

        public void RecordContextTask(Guid jobId)
        {
            ContextJobIds.Enqueue(jobId);
        }

        public void MarkShutdownTaskStarted()
        {
            shutdownTaskStarted.TrySetResult();
        }

        public async Task WaitForShutdownTaskStartAsync()
        {
            await shutdownTaskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class WorkerScopeMarker
    {
        public Guid ScopeId { get; } = Guid.NewGuid();
    }

    private sealed class WorkerTestService(WorkerTests.WorkerExecutionRecorder recorder, WorkerTests.WorkerScopeMarker marker)
    {
        public Task RecordAsync(string value, CancellationToken cancellationToken)
        {
            recorder.Record(marker.ScopeId, value);
            return Task.CompletedTask;
        }

        public async Task RecordWithContextAsync(string value, IJobContext jobContext, CancellationToken cancellationToken)
        {
            if (jobContext.CancellationToken != cancellationToken)
            {
                throw new InvalidOperationException("Job context token did not match the handler token.");
            }

            recorder.Record(marker.ScopeId, value);
            recorder.RecordContextTask(jobContext.JobId);
            await jobContext.LogAsync(JobLogLevel.Information, "context log", cancellationToken: cancellationToken);
            await jobContext.ReportProgressAsync(25, "context progress", cancellationToken);
        }

        public Task ThrowAsync(string message, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(message);
        }

        public async Task WaitForShutdownAsync(CancellationToken cancellationToken)
        {
            recorder.MarkShutdownTaskStarted();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
