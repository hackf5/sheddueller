namespace Sheddueller.Tests;

using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class WorkerTests
{
    [Fact]
    public async Task HostedWorker_QueuedTasks_ExecutesFromFreshScopesAndCompletes()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 13, 0, 0, TimeSpan.Zero);
        using var host = CreateHost(new ManualTimeProvider(timestamp));
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<ITaskEnqueuer>();
        var store = host.Services.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        var first = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordAsync("first", cancellationToken));
        var second = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordAsync("second", cancellationToken));

        await WaitUntilAsync(() =>
          store.GetSnapshot(first)?.State == TaskState.Completed
          && store.GetSnapshot(second)?.State == TaskState.Completed);

        recorder.Values.Order(StringComparer.Ordinal).ShouldBe(["first", "second"]);
        recorder.ScopeIds.Distinct().Count().ShouldBe(2);
        store.GetSnapshot(first).ShouldNotBeNull().CompletedAtUtc.ShouldBe(timestamp);
        store.GetSnapshot(second).ShouldNotBeNull().CompletedAtUtc.ShouldBe(timestamp);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostedWorker_ThrowingTask_RecordsFailureDetails()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<ITaskEnqueuer>();
        var store = host.Services.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();

        var taskId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.ThrowAsync("expected failure", cancellationToken));

        await WaitUntilAsync(() => store.GetSnapshot(taskId)?.State == TaskState.Failed);

        var snapshot = store.GetSnapshot(taskId).ShouldNotBeNull();
        snapshot.Failure.ShouldNotBeNull().ExceptionType.ShouldContain(nameof(InvalidOperationException));
        snapshot.Failure.Message.ShouldContain("expected failure");

        await host.StopAsync();
    }

    [Fact]
    public async Task HostedWorker_DirectStoreTask_ClaimsThroughFallbackPolling()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var store = host.Services.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();
        var serializer = host.Services.GetRequiredService<ITaskPayloadSerializer>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();
        var taskId = Guid.NewGuid();
        var payload = await serializer.SerializeAsync(new object?[] { "direct" }, [typeof(string)]);

        await store.EnqueueAsync(new EnqueueTaskRequest(
          taskId,
          0,
          typeof(WorkerTestService).AssemblyQualifiedName!,
          nameof(WorkerTestService.RecordAsync),
          [typeof(string).AssemblyQualifiedName!, typeof(CancellationToken).AssemblyQualifiedName!],
          payload,
          [],
          DateTimeOffset.UtcNow));

        await WaitUntilAsync(() => store.GetSnapshot(taskId)?.State == TaskState.Completed);

        recorder.Values.ShouldContain("direct");

        await host.StopAsync();
    }

    [Fact]
    public async Task HostShutdown_CooperativeCancellation_RequeuesWithoutRetryBudgetConsumption()
    {
        using var host = CreateHost();
        await host.StartAsync();
        var enqueuer = host.Services.GetRequiredService<ITaskEnqueuer>();
        var store = host.Services.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();
        var recorder = host.Services.GetRequiredService<WorkerExecutionRecorder>();

        var taskId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.WaitForShutdownAsync(cancellationToken));

        await recorder.WaitForShutdownTaskStartAsync();
        await host.StopAsync();

        var snapshot = store.GetSnapshot(taskId).ShouldNotBeNull();
        snapshot.State.ShouldBe(TaskState.Queued);
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
    public async Task TaskManager_QueuedTask_CancelsTask()
    {
        using var host = CreateHost();
        var enqueuer = host.Services.GetRequiredService<ITaskEnqueuer>();
        var taskManager = host.Services.GetRequiredService<ITaskManager>();
        var store = host.Services.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();

        var taskId = await enqueuer.EnqueueAsync<WorkerTestService>(
          (service, cancellationToken) => service.RecordAsync("cancel-me", cancellationToken),
          new TaskSubmission(NotBeforeUtc: DateTimeOffset.UtcNow.AddHours(1)));

        (await taskManager.CancelAsync(taskId)).ShouldBeTrue();
        store.GetSnapshot(taskId).ShouldNotBeNull().State.ShouldBe(TaskState.Canceled);
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

        public void Record(Guid scopeId, string value)
        {
            Values.Enqueue(value);
            ScopeIds.Enqueue(scopeId);
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
