using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sheddueller;

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
    private readonly SemaphoreSlim signal = new(0);
    private int signaled;

    public void Notify()
    {
        if (Interlocked.Exchange(ref signaled, 1) == 0)
        {
            signal.Release();
        }
    }

    public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (await signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            Volatile.Write(ref signaled, 0);
        }
    }

    public void Dispose()
    {
        signal.Dispose();
    }
}

internal sealed class ShedduellerNodeIdProvider : IShedduellerNodeIdProvider
{
    public ShedduellerNodeIdProvider(IOptions<ShedduellerOptions> options)
    {
        var configuredNodeId = options.Value.NodeId;
        NodeId = string.IsNullOrWhiteSpace(configuredNodeId)
          ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
          : configuredNodeId;
    }

    public string NodeId { get; }
}

internal sealed class ShedduellerStartupValidator(IServiceProvider serviceProvider, IOptions<ShedduellerOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var value = options.Value;

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

        if (serviceProvider.GetService<ITaskStore>() is null)
        {
            throw new InvalidOperationException("No Sheddueller task store provider has been registered.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class ShedduellerWorker(
  IServiceProvider serviceProvider,
  IServiceScopeFactory scopeFactory,
  IOptions<ShedduellerOptions> options,
  TimeProvider timeProvider,
  IShedduellerWakeSignal wakeSignal,
  IShedduellerNodeIdProvider nodeIdProvider) : BackgroundService
{
    private readonly ConcurrentDictionary<Task, byte> runningTasks = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var store = serviceProvider.GetRequiredService<ITaskStore>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                PruneCompletedTasks();

                var claimedTask = false;
                while (!stoppingToken.IsCancellationRequested && runningTasks.Count < options.Value.MaxConcurrentExecutionsPerNode)
                {
                    var claimResult = await store
                      .TryClaimNextAsync(new ClaimTaskRequest(nodeIdProvider.NodeId, timeProvider.GetUtcNow()), stoppingToken)
                      .ConfigureAwait(false);

                    if (claimResult is not ClaimTaskResult.Claimed claimed)
                    {
                        break;
                    }

                    claimedTask = true;
                    TrackTask(ExecuteClaimedTaskAsync(store, claimed.Task, stoppingToken));
                }

                if (claimedTask)
                {
                    continue;
                }

                await WaitForWorkOrCapacityAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown stops claiming. Running tasks are awaited below so terminal state can be recorded.
        }

        await WaitForRunningTasksAsync().ConfigureAwait(false);
    }

    private async ValueTask WaitForWorkOrCapacityAsync(CancellationToken stoppingToken)
    {
        if (runningTasks.IsEmpty)
        {
            await wakeSignal.WaitAsync(options.Value.IdlePollingInterval, stoppingToken).ConfigureAwait(false);
            return;
        }

        var delayTask = Task.Delay(options.Value.IdlePollingInterval, stoppingToken);
        var signalTask = wakeSignal.WaitAsync(options.Value.IdlePollingInterval, stoppingToken).AsTask();
        var completedRunningTask = await Task.WhenAny(runningTasks.Keys.Append(delayTask).Append(signalTask)).ConfigureAwait(false);

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
            await InvokeClaimedTaskAsync(task, executionToken).ConfigureAwait(false);
            await store
              .MarkCompletedAsync(new CompleteTaskRequest(task.TaskId, nodeIdProvider.NodeId, timeProvider.GetUtcNow()), CancellationToken.None)
              .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await store
              .MarkFailedAsync(
                new FailTaskRequest(task.TaskId, nodeIdProvider.NodeId, timeProvider.GetUtcNow(), CreateFailureInfo(exception)),
                CancellationToken.None)
              .ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeClaimedTaskAsync(ClaimedTask task, CancellationToken executionToken)
    {
        var serviceType = TypeNameFormatter.Resolve(task.ServiceType);
        var methodParameterTypes = task.MethodParameterTypes.Select(TypeNameFormatter.Resolve).ToArray();
        var serializableParameterTypes = methodParameterTypes.Where(type => type != typeof(CancellationToken)).ToArray();

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var service = scope.ServiceProvider.GetRequiredService(serviceType);
            var method = serviceType.GetMethod(
              task.MethodName,
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
              binder: null,
              types: methodParameterTypes,
              modifiers: null);

            if (method is null)
            {
                throw new InvalidOperationException($"Could not resolve task method '{task.MethodName}' on service type '{serviceType}'.");
            }

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
        runningTasks.TryAdd(task, 0);
        task.ContinueWith(
          completedTask => runningTasks.TryRemove(completedTask, out _),
          CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
    }

    private void PruneCompletedTasks()
    {
        foreach (var task in runningTasks.Keys.Where(task => task.IsCompleted))
        {
            runningTasks.TryRemove(task, out _);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Individual execution tasks persist failure details before shutdown wait observes them.")]
    private async Task WaitForRunningTasksAsync()
    {
        while (!runningTasks.IsEmpty)
        {
            Task[] snapshot = [.. runningTasks.Keys];
            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch
            {
                // Individual execution tasks record their own failure state.
            }

            PruneCompletedTasks();
        }
    }

    private static TaskFailureInfo CreateFailureInfo(Exception exception)
    {
        return new TaskFailureInfo(
          exception.GetType().FullName ?? exception.GetType().Name,
          exception.Message,
          exception.StackTrace);
    }
}
