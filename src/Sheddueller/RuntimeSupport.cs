namespace Sheddueller;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal interface IShedduellerWakeSignal
{
    void Notify();

    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

internal interface IShedduellerNodeIdProvider
{
    string NodeId { get; }
}

internal sealed class ShedduellerWakeSignal : IShedduellerWakeSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0);
    private int _signaled;

    public void Notify()
    {
        if (Interlocked.Exchange(ref this._signaled, 1) == 0)
        {
            this._signal.Release();
        }
    }

    public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (await this._signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            Volatile.Write(ref this._signaled, 0);
        }
    }

    public void Dispose()
      => this._signal.Dispose();
}

internal sealed class ShedduellerNodeIdProvider : IShedduellerNodeIdProvider
{
    public ShedduellerNodeIdProvider(IOptions<ShedduellerOptions> options)
    {
        var configuredNodeId = options.Value.NodeId;
        this.NodeId = string.IsNullOrWhiteSpace(configuredNodeId)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : configuredNodeId;
    }

    public string NodeId { get; }
}

internal sealed class ShedduellerStartupValidator(
    IServiceProvider serviceProvider,
    IOptions<ShedduellerOptions> options) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IOptions<ShedduellerOptions> _options = options;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var value = this._options.Value;

        if (value.NodeId is not null && value.NodeId.Length == 0)
        {
            throw new InvalidOperationException("ShedduellerOptions.NodeId must be null or a non-empty string.");
        }

        if (value.MaxConcurrentExecutionsPerNode <= 0)
        {
            throw new InvalidOperationException("ShedduellerOptions.MaxConcurrentExecutionsPerNode must be positive.");
        }

        if (value.IdlePollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ShedduellerOptions.IdlePollingInterval must be positive.");
        }

        if (this._serviceProvider.GetService<ITaskStore>() is null)
        {
            throw new InvalidOperationException("No Sheddueller task store provider has been registered.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
      => Task.CompletedTask;
}

internal sealed class ShedduellerWorker(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    IOptions<ShedduellerOptions> options,
    TimeProvider timeProvider,
    IShedduellerWakeSignal wakeSignal,
    IShedduellerNodeIdProvider nodeIdProvider) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptions<ShedduellerOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IShedduellerWakeSignal _wakeSignal = wakeSignal;
    private readonly IShedduellerNodeIdProvider _nodeIdProvider = nodeIdProvider;
    private readonly ConcurrentDictionary<Task, byte> _runningTasks = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var store = this._serviceProvider.GetRequiredService<ITaskStore>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                this.PruneCompletedTasks();

                var claimedTask = false;
                while (!stoppingToken.IsCancellationRequested && this._runningTasks.Count < this._options.Value.MaxConcurrentExecutionsPerNode)
                {
                    var claimResult = await store
                      .TryClaimNextAsync(new ClaimTaskRequest(this._nodeIdProvider.NodeId, this._timeProvider.GetUtcNow()), stoppingToken)
                      .ConfigureAwait(false);

                    if (claimResult is not ClaimTaskResult.Claimed claimed)
                    {
                        break;
                    }

                    claimedTask = true;
                    this.TrackTask(this.ExecuteClaimedTaskAsync(store, claimed.Task, stoppingToken));
                }

                if (claimedTask)
                {
                    continue;
                }

                await this.WaitForWorkOrCapacityAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown stops claiming. Running tasks are awaited below so terminal state can be recorded.
        }

        await this.WaitForRunningTasksAsync().ConfigureAwait(false);
    }

    private async ValueTask WaitForWorkOrCapacityAsync(CancellationToken stoppingToken)
    {
        if (this._runningTasks.IsEmpty)
        {
            await this._wakeSignal.WaitAsync(this._options.Value.IdlePollingInterval, stoppingToken).ConfigureAwait(false);
            return;
        }

        var delayTask = Task.Delay(this._options.Value.IdlePollingInterval, stoppingToken);
        var signalTask = this._wakeSignal.WaitAsync(this._options.Value.IdlePollingInterval, stoppingToken).AsTask();
        var completedRunningTask = await Task.WhenAny(this._runningTasks.Keys.Append(delayTask).Append(signalTask)).ConfigureAwait(false);

        if (completedRunningTask == signalTask)
        {
            await signalTask.ConfigureAwait(false);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Task failures must be persisted instead of escaping the worker.")]
    private async Task ExecuteClaimedTaskAsync(ITaskStore store, ClaimedTask task, CancellationToken executionToken)
    {
        try
        {
            await this.InvokeClaimedTaskAsync(task, executionToken).ConfigureAwait(false);
            await store
              .MarkCompletedAsync(new CompleteTaskRequest(task.TaskId, this._nodeIdProvider.NodeId, this._timeProvider.GetUtcNow()), CancellationToken.None)
              .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await store
              .MarkFailedAsync(
          new FailTaskRequest(task.TaskId, this._nodeIdProvider.NodeId, this._timeProvider.GetUtcNow(), CreateFailureInfo(exception)),
          CancellationToken.None)
        .ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeClaimedTaskAsync(ClaimedTask task, CancellationToken executionToken)
    {
        var serviceType = TypeNameFormatter.Resolve(task.ServiceType);
        var methodParameterTypes = task.MethodParameterTypes.Select(TypeNameFormatter.Resolve).ToArray();
        var serializableParameterTypes = methodParameterTypes.Where(type => type != typeof(CancellationToken)).ToArray();

        var scope = this._scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var service = scope.ServiceProvider.GetRequiredService(serviceType);
            var method = serviceType.GetMethod(
              task.MethodName,
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
              binder: null,
              types: methodParameterTypes,
              modifiers: null);

            _ = method ?? throw new InvalidOperationException($"Could not resolve task method '{task.MethodName}' on service type '{serviceType}'.");

            var deserializedArguments = await scope.ServiceProvider
              .GetRequiredService<ITaskPayloadSerializer>()
              .DeserializeAsync(task.SerializedArguments, serializableParameterTypes, executionToken)
              .ConfigureAwait(false);
            var invocationArguments = BuildInvocationArguments(methodParameterTypes, deserializedArguments, executionToken);
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
                    throw new InvalidOperationException("Task method returned an unsupported result.");
            }
        }
    }

    private static object?[] BuildInvocationArguments(
      Type[] methodParameterTypes,
      IReadOnlyList<object?> deserializedArguments,
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

            invocationArguments[i] = deserializedArguments[deserializedIndex];
            deserializedIndex++;
        }

        if (deserializedIndex != deserializedArguments.Count)
        {
            throw new InvalidOperationException("Task payload argument count did not match target method parameters.");
        }

        return invocationArguments;
    }

    private void TrackTask(Task task)
    {
        this._runningTasks.TryAdd(task, 0);
        task.ContinueWith(
          completedTask => this._runningTasks.TryRemove(completedTask, out _),
              CancellationToken.None,
              TaskContinuationOptions.ExecuteSynchronously,
              TaskScheduler.Default);
    }

    private void PruneCompletedTasks()
    {
        foreach (var task in this._runningTasks.Keys.Where(task => task.IsCompleted))
        {
            this._runningTasks.TryRemove(task, out _);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Individual execution tasks persist failure details before shutdown wait observes them.")]
    private async Task WaitForRunningTasksAsync()
    {
        while (!this._runningTasks.IsEmpty)
        {
            Task[] snapshot = [.. this._runningTasks.Keys];
            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch
            {
                // Individual execution tasks record their own failure state.
            }

            this.PruneCompletedTasks();
        }
    }

    private static TaskFailureInfo CreateFailureInfo(Exception exception)
      => new(
        exception.GetType().FullName ?? exception.GetType().Name,
        exception.Message,
        exception.StackTrace);
}
