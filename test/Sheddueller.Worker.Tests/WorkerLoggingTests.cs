namespace Sheddueller.Worker.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sheddueller.Serialization;
using Sheddueller.Storage;
using Sheddueller.Tests.Logging;
using Sheddueller.Worker.Internal;

using Shouldly;

public sealed class WorkerLoggingTests
{
    [Fact]
    public async Task JobExecution_Exception_LogsFailureAndMarksJobFailed()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var job = CreateClaimedJob();
        var store = new SingleClaimJobStore(job);
        using var logs = new TestLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
          .SetMinimumLevel(LogLevel.Trace)
          .AddProvider(logs));
        services.AddSingleton(store);
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<SingleClaimJobStore>());
        services.AddTransient<FailingJob>();
        services.AddShedduellerWorker(builder => builder.ConfigureOptions(options =>
        {
            options.NodeId = "worker-a";
            options.IdlePollingInterval = TimeSpan.FromMilliseconds(10);
            options.HeartbeatInterval = TimeSpan.FromSeconds(5);
            options.LeaseDuration = TimeSpan.FromSeconds(30);
        }));
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetServices<IHostedService>().OfType<ShedduellerWorker>().Single();

        await worker.StartAsync(cancellationTokenSource.Token);
        var failed = await store.Failed.Task.WaitAsync(cancellationTokenSource.Token);
        await worker.StopAsync(cancellationTokenSource.Token);

        failed.JobId.ShouldBe(job.JobId);
        failed.Failure.ExceptionType.ShouldBe(typeof(InvalidOperationException).FullName);

        var entry = logs.SingleByEventId(1121);
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeOfType<InvalidOperationException>();
        entry.Properties["JobId"].ShouldBe(job.JobId);
        entry.Properties["AttemptNumber"].ShouldBe(job.AttemptCount);
        entry.Properties["NodeId"].ShouldBe("worker-a");
        entry.MessageTemplate.ShouldBe("Job {JobId} attempt {AttemptNumber} failed on node {NodeId}.");
    }

    private static ClaimedJob CreateClaimedJob()
      => new(
          Guid.NewGuid(),
          EnqueueSequence: 1,
          Priority: 0,
          ServiceType: typeof(FailingJob).AssemblyQualifiedName!,
          MethodName: nameof(FailingJob.FailAsync),
          MethodParameterTypes: [typeof(CancellationToken).AssemblyQualifiedName!],
          SerializedArguments: new SerializedJobPayload(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray()),
          ConcurrencyGroupKeys: [],
          AttemptCount: 1,
          MaxAttempts: 1,
          LeaseToken: Guid.NewGuid(),
          LeaseExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(30),
          RetryBackoffKind: null,
          RetryBaseDelay: null,
          RetryMaxDelay: null,
          SourceScheduleKey: null,
          ScheduledFireAtUtc: null,
          MethodParameterBindings: [new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken)]);

    private sealed class FailingJob
    {
        public Task FailAsync(CancellationToken cancellationToken)
          => throw new InvalidOperationException("job failed");
    }

    private sealed class SingleClaimJobStore(ClaimedJob job) : IJobStore
    {
        private int _claimed;

        public TaskCompletionSource<FailJobRequest> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<EnqueueJobResult> EnqueueAsync(
            EnqueueJobRequest request,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<EnqueueJobResult>> EnqueueManyAsync(
            IReadOnlyList<EnqueueJobRequest> requests,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<ClaimJobResult> TryClaimNextAsync(
            ClaimJobRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<ClaimJobResult>(
              Interlocked.Exchange(ref this._claimed, 1) == 0
                ? new ClaimJobResult.Claimed(job)
                : new ClaimJobResult.NoJobAvailable());

        public ValueTask<bool> MarkCompletedAsync(
            CompleteJobRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(true);

        public ValueTask<bool> MarkFailedAsync(
            FailJobRequest request,
            CancellationToken cancellationToken = default)
        {
            this.Failed.TrySetResult(request);
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> RenewLeaseAsync(
            RenewLeaseRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(true);

        public ValueTask<bool> ReleaseJobAsync(
            ReleaseJobRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(true);

        public ValueTask<int> RecoverExpiredLeasesAsync(
            RecoverExpiredLeasesRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(0);

        public ValueTask<JobCancellationResult> CancelAsync(
            CancelJobRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(JobCancellationResult.NotFound);

        public ValueTask<DateTimeOffset?> GetCancellationRequestedAtAsync(
            JobCancellationStatusRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult<DateTimeOffset?>(null);

        public ValueTask<bool> MarkCancellationObservedAsync(
            ObserveJobCancellationRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(true);

        public ValueTask RecordWorkerNodeHeartbeatAsync(
            WorkerNodeHeartbeatRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.CompletedTask;

        public ValueTask SetConcurrencyLimitAsync(
            SetConcurrencyLimitRequest request,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
            string groupKey,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
            UpsertRecurringScheduleRequest request,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleTriggerResult> TriggerRecurringScheduleAsync(
            TriggerRecurringScheduleRequest request,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<bool> DeleteRecurringScheduleAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<bool> PauseRecurringScheduleAsync(
            string scheduleKey,
            DateTimeOffset pausedAtUtc,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<bool> ResumeRecurringScheduleAsync(
            string scheduleKey,
            DateTimeOffset resumedAtUtc,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<RecurringScheduleInfo?> GetRecurringScheduleAsync(
            string scheduleKey,
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RecurringScheduleInfo>> ListRecurringSchedulesAsync(
            CancellationToken cancellationToken = default)
          => throw new NotSupportedException();

        public ValueTask<int> MaterializeDueRecurringSchedulesAsync(
            MaterializeDueRecurringSchedulesRequest request,
            CancellationToken cancellationToken = default)
          => ValueTask.FromResult(0);
    }
}
