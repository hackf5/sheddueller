namespace Sheddueller.Runtime;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Sheddueller.Dashboard;
using Sheddueller.DependencyInjection;
using Sheddueller.Enqueueing;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class ShedduellerWorker(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    IOptions<ShedduellerOptions> options,
    TimeProvider timeProvider,
    IShedduellerWakeSignal wakeSignal,
    IShedduellerNodeIdProvider nodeIdProvider,
    IDashboardEventSink dashboardEventSink) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptions<ShedduellerOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IShedduellerWakeSignal _wakeSignal = wakeSignal;
    private readonly IShedduellerNodeIdProvider _nodeIdProvider = nodeIdProvider;
    private readonly IDashboardEventSink _dashboardEventSink = dashboardEventSink;
    private readonly ConcurrentDictionary<Task, byte> _runningJobs = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var store = this._serviceProvider.GetRequiredService<IJobStore>();

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

        await this.WaitForRunningJobsAsync().ConfigureAwait(false);
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
        var heartbeatTask = this.RenewLeaseUntilStoppedAsync(store, job, executionTokenSource);

        try
        {
            await this.InvokeClaimedJobAsync(job, executionTokenSource.Token).ConfigureAwait(false);
            await store
                .MarkCompletedAsync(new CompleteJobRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow()), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (executionTokenSource.IsCancellationRequested)
        {
            await store
                .ReleaseJobAsync(new ReleaseJobRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow()), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await store
                .MarkFailedAsync(
                    new FailJobRequest(job.JobId, this._nodeIdProvider.NodeId, job.LeaseToken, this._timeProvider.GetUtcNow(), CreateFailureInfo(exception)),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await executionTokenSource.CancelAsync().ConfigureAwait(false);
            await WaitForHeartbeatTaskAsync(heartbeatTask).ConfigureAwait(false);
            executionTokenSource.Dispose();
        }
    }

    private async ValueTask InvokeClaimedJobAsync(ClaimedJob job, CancellationToken executionToken)
    {
        var serviceType = TypeNameFormatter.Resolve(job.ServiceType);
        var methodParameterTypes = job.MethodParameterTypes.Select(TypeNameFormatter.Resolve).ToArray();
        var serializableParameterTypes = methodParameterTypes.Where(type => type != typeof(CancellationToken) && type != typeof(IJobContext)).ToArray();
        var jobContext = new JobContext(job.JobId, job.AttemptCount, this._dashboardEventSink, executionToken);

        var scope = this._scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var service = scope.ServiceProvider.GetRequiredService(serviceType);
            var method = serviceType.GetMethod(
                job.MethodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: methodParameterTypes,
                modifiers: null);

            _ = method ?? throw new InvalidOperationException($"Could not resolve job method '{job.MethodName}' on service type '{serviceType}'.");

            var deserializedArguments = await scope.ServiceProvider
                .GetRequiredService<IJobPayloadSerializer>()
                .DeserializeAsync(job.SerializedArguments, serializableParameterTypes, executionToken)
                .ConfigureAwait(false);
            var invocationArguments = BuildInvocationArguments(methodParameterTypes, deserializedArguments, jobContext, executionToken);
            object? result;

            try
            {
                result = method.Invoke(service, invocationArguments);
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
        Type[] methodParameterTypes,
        IReadOnlyList<object?> deserializedArguments,
        IJobContext jobContext,
        CancellationToken executionToken)
    {
        var invocationArguments = new object?[methodParameterTypes.Length];
        var deserializedIndex = 0;

        for (var i = 0; i < methodParameterTypes.Length; i++)
        {
            if (methodParameterTypes[i] == typeof(CancellationToken))
            {
                invocationArguments[i] = executionToken;
                continue;
            }

            if (methodParameterTypes[i] == typeof(IJobContext))
            {
                invocationArguments[i] = jobContext;
                continue;
            }

            invocationArguments[i] = deserializedArguments[deserializedIndex];
            deserializedIndex++;
        }

        if (deserializedIndex != deserializedArguments.Count)
        {
            throw new InvalidOperationException("Job payload argument count did not match target method parameters.");
        }

        return invocationArguments;
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
        var recovered = await store
            .RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(now), cancellationToken)
            .ConfigureAwait(false);
        var materialized = await store
            .MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(now, this._options.Value.DefaultRetryPolicy), cancellationToken)
            .ConfigureAwait(false);

        if (recovered > 0 || materialized > 0)
        {
            this._wakeSignal.Notify();
        }
    }

    private async Task RenewLeaseUntilStoppedAsync(
        IJobStore store,
        ClaimedJob job,
        CancellationTokenSource executionTokenSource)
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
    private static async ValueTask WaitForHeartbeatTaskAsync(Task heartbeatTask)
    {
        try
        {
            await heartbeatTask.ConfigureAwait(false);
        }
        catch
        {
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
}
