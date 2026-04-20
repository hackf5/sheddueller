namespace Sheddueller;

using System.Linq.Expressions;

/// <summary>
/// Enqueues work for asynchronous execution by Sheddueller workers.
/// </summary>
public interface IJobEnqueuer
{
    /// <summary>
    /// Enqueues a Task-returning service method call.
    /// </summary>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, Task>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a ValueTask-returning service method call.
    /// </summary>
    ValueTask<Guid> EnqueueAsync<TService>(
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        JobSubmission? submission = null,
        CancellationToken cancellationToken = default);
}
