namespace Sheddueller;

using System.Linq.Expressions;

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

    /// <summary>
    /// Enqueues a job-context-aware task-returning service method call.
    /// </summary>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, IJobContext, Task>> work,
        TaskSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a job-context-aware value-task-returning service method call.
    /// </summary>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, IJobContext, ValueTask>> work,
        TaskSubmission? submission = null,
        CancellationToken cancellationToken = default);
}
