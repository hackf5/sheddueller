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
          TimeProvider.System,
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

    [Fact]
    public async Task Cleanup_PersistedSettingsStore_UsesPersistedRetention()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new RecordingRetentionStore(0);
        var settingsStore = new RecordingCleanupConfigurationStore(new JobEventCleanupConfiguration(
          TimeSpan.FromDays(3),
          TimeSpan.FromHours(1)));
        var services = new ServiceCollection();
        services.AddSingleton<IJobEventRetentionStore>(store);
        services.AddSingleton<IShedduellerCleanupConfigurationStore>(settingsStore);
        using var serviceProvider = services.BuildServiceProvider();
        using var service = new JobEventRetentionService(
          serviceProvider,
          TimeProvider.System,
          Options.Create(new ShedduellerDashboardOptions { EventRetention = TimeSpan.FromDays(1) }),
          Microsoft.Extensions.Logging.Abstractions.NullLogger<JobEventRetentionService>.Instance);

        await service.StartAsync(cancellationTokenSource.Token);
        await store.CleanupCalled.Task.WaitAsync(cancellationTokenSource.Token);
        await service.StopAsync(cancellationTokenSource.Token);

        store.Retention.ShouldBe(TimeSpan.FromDays(3));
    }

    private sealed class RecordingRetentionStore(int deletedCount) : IJobEventRetentionStore
    {
        public TaskCompletionSource CleanupCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TimeSpan? Retention { get; private set; }

        public ValueTask<int> CleanupAsync(
            TimeSpan retention,
            CancellationToken cancellationToken = default)
        {
            this.Retention = retention;
            this.CleanupCalled.TrySetResult();
            return ValueTask.FromResult(deletedCount);
        }
    }

    private sealed class RecordingCleanupConfigurationStore(JobEventCleanupConfiguration configuration) : IShedduellerCleanupConfigurationStore
    {
        public ValueTask<ShedduellerCleanupConfiguration> GetCleanupConfigurationAsync(
            ShedduellerCleanupConfiguration defaultConfiguration,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(defaultConfiguration with { JobEvents = configuration });

        public ValueTask<JobRetentionCleanupConfiguration> GetJobRetentionCleanupConfigurationAsync(
            JobRetentionCleanupConfiguration defaultConfiguration,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(defaultConfiguration);

        public ValueTask<JobEventCleanupConfiguration> GetJobEventCleanupConfigurationAsync(
            JobEventCleanupConfiguration defaultConfiguration,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(configuration);

        public ValueTask<MetricsCleanupConfiguration> GetMetricsCleanupConfigurationAsync(
            MetricsCleanupConfiguration defaultConfiguration,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(defaultConfiguration);

        public ValueTask SetCleanupConfigurationAsync(
            ShedduellerCleanupConfiguration configuration,
            CancellationToken cancellationToken = default)
          => ValueTask.CompletedTask;
    }
}
