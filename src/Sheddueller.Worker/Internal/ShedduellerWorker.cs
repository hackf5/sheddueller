namespace Sheddueller.Worker.Internal;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheddueller;
using Sheddueller.Enqueueing;
using Sheddueller.Runtime;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class ShedduellerWorker(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    IOptions<ShedduellerOptions> options,
    TimeProvider timeProvider,
    IShedduellerWakeSignal wakeSignal,
    IShedduellerNodeIdProvider nodeIdProvider,
    IJobEventSink jobEventSink,
    ILogger<ShedduellerWorker> logger,
    ILogger<JobContext> jobContextLogger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptions<ShedduellerOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IShedduellerWakeSignal _wakeSignal = wakeSignal;
    private readonly IShedduellerNodeIdProvider _nodeIdProvider = nodeIdProvider;
    private readonly IJobEventSink _jobEventSink = jobEventSink;
    private readonly ILogger<ShedduellerWorker> _logger = logger;
    private readonly ILogger<JobContext> _jobContextLogger = jobContextLogger;
    private readonly ConcurrentDictionary<Task, byte> _runningJobs = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var store = this._serviceProvider.GetRequiredService<IJobStore>();
        this._logger.WorkerStarted(this._nodeIdProvider.NodeId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                this.PruneCompletedJobs();
                await this.RunPeriodicStoreWorkAsync(store, stoppingToken).ConfigureAwait(false);

                var claimedJob = false;
                while (!stoppingToken.IsCancellationRequested && this._runningJobs.Count < this._options.Value.MaxConcurrentExecutionsPerNode)
                {
                    var now = this._timeProvider.GetUtcNow();
                    var claimResult = await store
                        .TryClaimNextAsync(new ClaimJobRequest(this._nodeIdProvider.NodeId, now, now.Add(this._options.Value.LeaseDuration)), stoppingToken)
                        .ConfigureAwait(false);

                    if (claimResult is not ClaimJobResult.Claimed claimed)
                    {
                        break;
                    }

                    claimedJob = true;
                    this._logger.JobClaimed(claimed.Job.JobId, claimed.Job.AttemptCount, this._nodeIdProvider.NodeId);
                    this.TrackRunningJob(this.ExecuteClaimedJobAsync(store, claimed.Job, stoppingToken));
                }

                if (claimedJob)
                {
                    continue;
                }

                await this.WaitForWorkOrCapacityAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown stops claiming. Running jobs are awaited below so terminal state can be recorded.
        }
        catch (Exception exception)
        {
            this._logger.WorkerFailed(exception, this._nodeIdProvider.NodeId);
            throw;
        }

        await this.WaitForRunningJobsAsync().ConfigureAwait(false);
        this._logger.WorkerStopped(this._nodeIdProvider.NodeId);
    }

    private async ValueTask WaitForWorkOrCapacityAsync(CancellationToken stoppingToken)
    {
        if (this._runningJobs.IsEmpty)
        {
            await this._wakeSignal.WaitAsync(this._options.Value.IdlePollingInterval, stoppingToken).ConfigureAwait(false);
            return;
        }

        var delayTask = Task.Delay(this._options.Value.IdlePollingInterval, stoppingToken);
        var signalTask = this._wakeSignal.WaitAsync(this._options.Value.IdlePollingInterval, stoppingToken).AsTask();
        var completedRunningTask = await Task.WhenAny(this._runningJobs.Keys.Append(delayTask).Append(signalTask)).ConfigureAwait(false);

        if (completedRunningTask == signalTask)
        {
            await signalTask.ConfigureAwait(false);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Job failures must be persisted instead of escaping the worker.")]
    private async Task ExecuteClaimedJobAsync(IJobStore store, ClaimedJob job, CancellationToken stoppingToken)
    {
        var executionTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var cancellationState = new RunningJobCancellationState();
        var heartbeatTask = this.RenewLeaseUntilStoppedAsync(store, job, executionTokenSource, cancellationState);

        try
        {
            await this.InvokeClaimedJobAsync(job, executionTokenSource.Token).ConfigureAwait(false);
            var completed = await store
                .MarkCompletedAsync(new CompleteJobRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow()), CancellationToken.None)
                .ConfigureAwait(false);
            if (completed)
            {
                this._logger.JobCompleted(job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
            }
        }
        catch (OperationCanceledException) when (executionTokenSource.IsCancellationRequested)
        {
            if (cancellationState.CancellationRequestedAtUtc is not null)
            {
                var observed = await store
                    .MarkCancellationObservedAsync(
                        new ObserveJobCancellationRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow()),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (observed)
                {
                    this._logger.JobCancellationObserved(job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
                }
            }
            else
            {
                var released = await store
                    .ReleaseJobAsync(new ReleaseJobRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow()), CancellationToken.None)
                    .ConfigureAwait(false);
                if (released)
                {
                    this._logger.JobReleased(job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
                }
            }
        }
        catch (Exception exception)
        {
            this._logger.JobFailed(exception, job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
            await store
                .MarkFailedAsync(
                    new FailJobRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow(), CreateFailureInfo(exception)),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await executionTokenSource.CancelAsync().ConfigureAwait(false);
            await this.WaitForHeartbeatTaskAsync(heartbeatTask, job).ConfigureAwait(false);
            executionTokenSource.Dispose();
        }
    }

    private async ValueTask InvokeClaimedJobAsync(ClaimedJob job, CancellationToken executionToken)
    {
        var serviceType = TypeNameFormatter.Resolve(job.ServiceType);
        var methodParameterTypes = job.MethodParameterTypes.Select(TypeNameFormatter.Resolve).ToArray();
        var parameterBindings = NormalizeParameterBindings(methodParameterTypes, job.MethodParameterBindings);
        var serializableParameterTypes = methodParameterTypes
          .Where((_, index) => parameterBindings[index].Kind == JobMethodParameterBindingKind.Serialized)
          .ToArray();
        var jobContext = new JobContext(job.JobId, job.AttemptCount, this._jobEventSink, this._jobContextLogger, executionToken);

        var scope = this._scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var (target, bindingFlags) = job.InvocationTargetKind switch
            {
                JobInvocationTargetKind.Static => (
                  Target: null,
                  BindingFlags: System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
                JobInvocationTargetKind.Instance => (
                  Target: scope.ServiceProvider.GetRequiredService(serviceType),
                  BindingFlags: System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
                _ => throw new InvalidOperationException($"Job invocation target kind '{job.InvocationTargetKind}' is not supported."),
            };
            var method = serviceType.GetMethod(
                job.MethodName,
                bindingFlags,
                binder: null,
                types: methodParameterTypes,
                modifiers: null);

            _ = method ?? throw new InvalidOperationException($"Could not resolve job method '{job.MethodName}' on service type '{serviceType}'.");

            var deserializedArguments = await scope.ServiceProvider
                .GetRequiredService<IJobPayloadSerializer>()
                .DeserializeAsync(job.SerializedArguments, serializableParameterTypes, executionToken)
                .ConfigureAwait(false);
            var progressReporter = new DecimalJobProgressReporter(
              job.JobId,
              job.AttemptCount,
              this._jobEventSink,
              this._jobContextLogger);
            var invocationArguments = BuildInvocationArguments(
              scope.ServiceProvider,
              methodParameterTypes,
              parameterBindings,
              deserializedArguments,
              jobContext,
              progressReporter,
              executionToken);
            object? result;

            try
            {
                result = method.Invoke(target, invocationArguments);
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            switch (result)
            {
                case Task taskResult:
                    await taskResult.ConfigureAwait(false);
                    break;
                case ValueTask valueTaskResult:
                    await valueTaskResult.ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Job method returned an unsupported result.");
            }
        }
    }

    private static object?[] BuildInvocationArguments(
        IServiceProvider serviceProvider,
        Type[] methodParameterTypes,
        IReadOnlyList<JobMethodParameterBinding> parameterBindings,
        IReadOnlyList<object?> deserializedArguments,
        IJobContext jobContext,
        DecimalJobProgressReporter progressReporter,
        CancellationToken executionToken)
    {
        if (methodParameterTypes.Length != parameterBindings.Count)
        {
            throw new InvalidOperationException("Job parameter binding count did not match target method parameters.");
        }

        var invocationArguments = new object?[methodParameterTypes.Length];
        var deserializedIndex = 0;

        for (var i = 0; i < methodParameterTypes.Length; i++)
        {
            switch (parameterBindings[i].Kind)
            {
                case JobMethodParameterBindingKind.CancellationToken:
                    invocationArguments[i] = executionToken;
                    continue;

                case JobMethodParameterBindingKind.JobContext:
                    invocationArguments[i] = jobContext;
                    continue;

                case JobMethodParameterBindingKind.ProgressReporter:
                    invocationArguments[i] = GetProgressReporter(methodParameterTypes[i], progressReporter);
                    continue;

                case JobMethodParameterBindingKind.Service:
                    invocationArguments[i] = ResolveBoundService(serviceProvider, methodParameterTypes[i], parameterBindings[i]);
                    continue;

                case JobMethodParameterBindingKind.Serialized:
                    invocationArguments[i] = deserializedArguments[deserializedIndex];
                    deserializedIndex++;
                    continue;

                default:
                    throw new InvalidOperationException($"Job parameter binding kind '{parameterBindings[i].Kind}' is not supported.");
            }
        }

        if (deserializedIndex != deserializedArguments.Count)
        {
            throw new InvalidOperationException("Job payload argument count did not match target method parameters.");
        }

        return invocationArguments;
    }

    private static DecimalJobProgressReporter GetProgressReporter(
        Type parameterType,
        DecimalJobProgressReporter progressReporter)
    {
        if (parameterType != typeof(IProgress<decimal>))
        {
            throw new InvalidOperationException($"Progress reporter parameter type '{parameterType}' is not supported.");
        }

        return progressReporter;
    }

    private static object ResolveBoundService(
        IServiceProvider serviceProvider,
        Type parameterType,
        JobMethodParameterBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.ServiceType))
        {
            throw new InvalidOperationException("Service parameter binding did not include a service type.");
        }

        var serviceType = TypeNameFormatter.Resolve(binding.ServiceType);
        if (!parameterType.IsAssignableFrom(serviceType))
        {
            throw new InvalidOperationException($"Resolved service type '{serviceType}' is not assignable to method parameter type '{parameterType}'.");
        }

        return serviceProvider.GetRequiredService(serviceType);
    }

    private static IReadOnlyList<JobMethodParameterBinding> NormalizeParameterBindings(
        Type[] methodParameterTypes,
        IReadOnlyList<JobMethodParameterBinding>? parameterBindings)
      => JobMethodParameterBindingResolver.Normalize(methodParameterTypes, parameterBindings);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Job progress telemetry is best-effort by v4 design.")]
    private sealed class DecimalJobProgressReporter(
        Guid jobId,
        int attemptNumber,
        IJobEventSink eventSink,
        ILogger logger) : IProgress<decimal>
    {
        public void Report(decimal value)
        {
            var request = new AppendJobEventRequest(
              jobId,
              JobEventKind.Progress,
              attemptNumber,
              ProgressPercent: (double)Math.Clamp(value, 0, 100));

            try
            {
                eventSink.AppendAsync(request).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                logger.JobEventAppendFailed(exception, jobId);
            }
        }
    }

    private void TrackRunningJob(Task executionTask)
    {
        this._runningJobs.TryAdd(executionTask, 0);
        executionTask.ContinueWith(
            completedTask => this._runningJobs.TryRemove(completedTask, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async ValueTask RunPeriodicStoreWorkAsync(IJobStore store, CancellationToken cancellationToken)
    {
        var now = this._timeProvider.GetUtcNow();
        await store
            .RecordWorkerNodeHeartbeatAsync(
                new WorkerNodeHeartbeatRequest(
                    this._nodeIdProvider.NodeId,
                    now,
                    this._options.Value.MaxConcurrentExecutionsPerNode,
                    this._runningJobs.Count),
                cancellationToken)
            .ConfigureAwait(false);

        var recovered = await store
            .RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(now), cancellationToken)
            .ConfigureAwait(false);
        var materialized = await store
            .MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(now, this._options.Value.DefaultRetryPolicy), cancellationToken)
            .ConfigureAwait(false);

        if (recovered > 0 || materialized > 0)
        {
            this._wakeSignal.Notify();
            this._logger.WorkerPeriodicStoreWorkCompleted(this._nodeIdProvider.NodeId, recovered, materialized);
        }
    }

    private async Task RenewLeaseUntilStoppedAsync(
        IJobStore store,
        ClaimedJob job,
        CancellationTokenSource executionTokenSource,
        RunningJobCancellationState cancellationState)
    {
        try
        {
            while (!executionTokenSource.IsCancellationRequested)
            {
                await Task.Delay(this._options.Value.HeartbeatInterval, executionTokenSource.Token).ConfigureAwait(false);

                var now = this._timeProvider.GetUtcNow();
                var renewed = await store
                    .RenewLeaseAsync(
                        new RenewLeaseRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, now, now.Add(this._options.Value.LeaseDuration)),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (!renewed)
                {
                    this._logger.JobLeaseRenewalLost(job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
                    await executionTokenSource.CancelAsync().ConfigureAwait(false);
                    return;
                }

                var cancellationRequestedAtUtc = await store
                    .GetCancellationRequestedAtAsync(
                        new JobCancellationStatusRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, now),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (cancellationRequestedAtUtc is not null && cancellationState.CancellationRequestedAtUtc is null)
                {
                    cancellationState.CancellationRequestedAtUtc = cancellationRequestedAtUtc;
                    this._logger.JobCancellationRequestedObserved(job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
                    await executionTokenSource.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (executionTokenSource.IsCancellationRequested)
        {
            // Expected when the job completes, the host stops, or ownership is lost.
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Heartbeat failures should not fault the job execution cleanup path.")]
    private async ValueTask WaitForHeartbeatTaskAsync(Task heartbeatTask, ClaimedJob job)
    {
        try
        {
            await heartbeatTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this._logger.JobHeartbeatFailed(exception, job.JobId, job.AttemptCount, this._nodeIdProvider.NodeId);
            // Failure to renew will be handled by lease expiry recovery.
        }
    }

    private void PruneCompletedJobs()
    {
        foreach (var job in this._runningJobs.Keys.Where(job => job.IsCompleted))
        {
            this._runningJobs.TryRemove(job, out _);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Individual execution jobs persist failure details before shutdown wait observes them.")]
    private async Task WaitForRunningJobsAsync()
    {
        while (!this._runningJobs.IsEmpty)
        {
            Task[] snapshot = [.. this._runningJobs.Keys];
            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch
            {
                // Individual execution jobs record their own failure state.
            }

            this.PruneCompletedJobs();
        }
    }

    private static JobFailureInfo CreateFailureInfo(Exception exception)
        => new(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace);

    private sealed class RunningJobCancellationState
    {
        public DateTimeOffset? CancellationRequestedAtUtc { get; set; }
    }
}
