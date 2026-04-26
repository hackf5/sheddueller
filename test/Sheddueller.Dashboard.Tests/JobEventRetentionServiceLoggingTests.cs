namespace Sheddueller.Dashboard.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheddueller.Dashboard;
using Sheddueller.Dashboard.Internal;
using Sheddueller.Storage;
using Sheddueller.Tests.Logging;

using Shouldly;

public sealed class JobEventRetentionServiceLoggingTests
{
    [Fact]
    public async Task Cleanup_NonZeroDeletedCount_LogsCleanupCount()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new RecordingRetentionStore(3);
        var services = new ServiceCollection();
        services.AddSingleton<IJobEventRetentionStore>(store);
        using var serviceProvider = services.BuildServiceProvider();
        using var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
          .SetMinimumLevel(LogLevel.Trace)
          .AddProvider(logs));
        using var service = new JobEventRetentionService(
          serviceProvider,
          Options.Create(new ShedduellerDashboardOptions { EventRetention = TimeSpan.FromDays(1) }),
          loggerFactory.CreateLogger<JobEventRetentionService>());

        await service.StartAsync(cancellationTokenSource.Token);
        await store.CleanupCalled.Task.WaitAsync(cancellationTokenSource.Token);
        await service.StopAsync(cancellationTokenSource.Token);

        var entry = logs.SingleByEventId(1321);
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Properties["DeletedCount"].ShouldBe(3);
        entry.MessageTemplate.ShouldBe("Dashboard job-event retention cleanup deleted {DeletedCount} events.");
    }

    private sealed class RecordingRetentionStore(int deletedCount) : IJobEventRetentionStore
    {
        public TaskCompletionSource CleanupCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<int> CleanupAsync(
            TimeSpan retention,
            CancellationToken cancellationToken = default)
        {
            this.CleanupCalled.TrySetResult();
            return ValueTask.FromResult(deletedCount);
        }
    }
}
