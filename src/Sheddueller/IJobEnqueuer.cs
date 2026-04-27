namespace Sheddueller;

using System.Linq.Expressions;

/// <summary>
/// Enqueues work for asynchronous execution by Sheddueller workers.
/// </summary>
/// <remarks>
/// Submitted expressions must be a single method call returning <see cref="Task"/> or <see cref="ValueTask"/>
/// and must forward the scheduler-owned <see cref="CancellationToken"/>. Serializable arguments are evaluated
/// when the job is submitted.
/// </remarks>
public interface IJobEnqueuer
{
    /// <summary>
    /// Enqueues a Task-returning job method call.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync(
        Expression<Func<CancellationToken, Task>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a ValueTask-returning job method call.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync(
        Expression<Func<CancellationToken, ValueTask>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a Task-returning job method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync(
        Expression<Func<CancellationToken, IProgress<decimal>, Task>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a ValueTask-returning job method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync(
        Expression<Func<CancellationToken, IProgress<decimal>, ValueTask>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a Task-returning service method call.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, Task>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a ValueTask-returning service method call.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a Task-returning service method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, IProgress<decimal>, Task>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a ValueTask-returning service method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The identifier of the queued job, or the existing queued job when generated idempotency matches.</returns>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, IProgress<decimal>, ValueTask>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically enqueues multiple service method calls.
    /// </summary>
    /// <param name="jobs">The jobs to enqueue as a single storage operation.</param>
    /// <param name="cancellationToken">A token for canceling the enqueue operation.</param>
    /// <returns>The submitted job identifiers in the same order as <paramref name="jobs"/>.</returns>
    ValueTask<IReadOnlyList<Guid>> EnqueueManyAsync(
        IReadOnlyList<JobEnqueueItem> jobs,
        CancellationToken cancellationToken = default);
}
