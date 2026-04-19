using System.Linq.Expressions;

namespace Sheddueller;

/// <summary>
/// Enqueues work for asynchronous execution by Sheddueller workers.
/// </summary>
public interface ITaskEnqueuer
{
  /// <summary>
  /// Enqueues a task-returning service method call.
  /// </summary>
  ValueTask<Guid> EnqueueAsync<TService>(
    Expression<Func<TService, CancellationToken, Task>> work,
    TaskSubmission? submission = null,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Enqueues a value-task-returning service method call.
  /// </summary>
  ValueTask<Guid> EnqueueAsync<TService>(
    Expression<Func<TService, CancellationToken, ValueTask>> work,
    TaskSubmission? submission = null,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages dynamic cluster-wide concurrency group limits.
/// </summary>
public interface IConcurrencyGroupManager
{
  /// <summary>
  /// Sets the configured limit for a concurrency group.
  /// </summary>
  ValueTask SetLimitAsync(
    string groupKey,
    int limit,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the configured limit for a concurrency group, if one exists.
  /// </summary>
  ValueTask<int?> GetConfiguredLimitAsync(
    string groupKey,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores task state and performs atomic claim selection.
/// </summary>
public interface ITaskStore
{
  /// <summary>
  /// Enqueues a new task and assigns its enqueue sequence.
  /// </summary>
  ValueTask<EnqueueTaskResult> EnqueueAsync(
    EnqueueTaskRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Claims the next globally eligible task, if one is available.
  /// </summary>
  ValueTask<ClaimTaskResult> TryClaimNextAsync(
    ClaimTaskRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Marks a claimed task as completed.
  /// </summary>
  ValueTask MarkCompletedAsync(
    CompleteTaskRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Marks a claimed task as failed.
  /// </summary>
  ValueTask MarkFailedAsync(
    FailTaskRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Persists a configured concurrency-group limit.
  /// </summary>
  ValueTask SetConcurrencyLimitAsync(
    SetConcurrencyLimitRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the configured concurrency-group limit, if one exists.
  /// </summary>
  ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
    string groupKey,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes and deserializes captured task method arguments.
/// </summary>
public interface ITaskPayloadSerializer
{
  /// <summary>
  /// Serializes captured method arguments.
  /// </summary>
  ValueTask<SerializedTaskPayload> SerializeAsync(
    IReadOnlyList<object?> arguments,
    IReadOnlyList<Type> parameterTypes,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Deserializes captured method arguments.
  /// </summary>
  ValueTask<IReadOnlyList<object?>> DeserializeAsync(
    SerializedTaskPayload payload,
    IReadOnlyList<Type> parameterTypes,
    CancellationToken cancellationToken = default);
}
