namespace Sheddueller;

using System.Linq.Expressions;

/// <summary>
/// Describes a job to enqueue as part of an atomic batch.
/// </summary>
public sealed class JobEnqueueItem
{
    private readonly List<JobEnqueueItem> _prerequisites = [];

    private JobEnqueueItem(
      Type? serviceType,
      LambdaExpression work,
      JobSubmission? submission)
    {
        this.ServiceType = serviceType;
        this.Work = work;
        this.Submission = submission;
    }

    internal Type? ServiceType { get; }

    internal LambdaExpression Work { get; }

    internal JobSubmission? Submission { get; }

    internal IReadOnlyList<JobEnqueueItem> Prerequisites => this._prerequisites;

    /// <summary>
    /// Makes this job wait until the supplied prerequisite jobs are terminal.
    /// </summary>
    /// <param name="prerequisites">Jobs from the same atomic batch that must become terminal first.</param>
    /// <returns>This enqueue item.</returns>
    /// <remarks>
    /// Terminal prerequisites include completed, failed, and canceled jobs. Dependency graphs may contain
    /// multiple levels, but every referenced prerequisite must be submitted in the same batch.
    /// </remarks>
    public JobEnqueueItem DependsOn(IEnumerable<JobEnqueueItem> prerequisites)
    {
        ArgumentNullException.ThrowIfNull(prerequisites);

        foreach (var prerequisite in prerequisites)
        {
            ArgumentNullException.ThrowIfNull(prerequisite, nameof(prerequisites));
            if (ReferenceEquals(this, prerequisite))
            {
                throw new ArgumentException("A job cannot depend on itself.", nameof(prerequisites));
            }

            if (this._prerequisites.Contains(prerequisite))
            {
                throw new ArgumentException("A prerequisite job cannot be added more than once.", nameof(prerequisites));
            }

            this._prerequisites.Add(prerequisite);
        }

        return this;
    }

    /// <summary>
    /// Makes this job wait until the supplied prerequisite jobs are terminal.
    /// </summary>
    /// <param name="prerequisites">Jobs from the same atomic batch that must become terminal first.</param>
    /// <returns>This enqueue item.</returns>
    public JobEnqueueItem DependsOn(params JobEnqueueItem[] prerequisites)
      => this.DependsOn((IEnumerable<JobEnqueueItem>)prerequisites);

    /// <summary>
    /// Creates a batch item for a Task-returning job method call.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create(
        Expression<Func<CancellationToken, Task>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(serviceType: null, work, submission);
    }

    /// <summary>
    /// Creates a batch item for a ValueTask-returning job method call.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create(
        Expression<Func<CancellationToken, ValueTask>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(serviceType: null, work, submission);
    }

    /// <summary>
    /// Creates a batch item for a Task-returning job method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create(
        Expression<Func<CancellationToken, IProgress<decimal>, Task>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(serviceType: null, work, submission);
    }

    /// <summary>
    /// Creates a batch item for a ValueTask-returning job method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create(
        Expression<Func<CancellationToken, IProgress<decimal>, ValueTask>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(serviceType: null, work, submission);
    }

    /// <summary>
    /// Creates a batch item for a Task-returning service method call.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create<TService>(
        Expression<Func<TService, CancellationToken, Task>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(typeof(TService), work, submission);
    }

    /// <summary>
    /// Creates a batch item for a ValueTask-returning service method call.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create<TService>(
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(typeof(TService), work, submission);
    }

    /// <summary>
    /// Creates a batch item for a Task-returning service method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create<TService>(
        Expression<Func<TService, CancellationToken, IProgress<decimal>, Task>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(typeof(TService), work, submission);
    }

    /// <summary>
    /// Creates a batch item for a ValueTask-returning service method call with scheduler-supplied progress reporting.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when the job executes.</typeparam>
    /// <param name="work">The method-call expression to persist as a job.</param>
    /// <param name="submission">Optional queueing, retry, idempotency, and metadata settings.</param>
    /// <returns>A batch enqueue item.</returns>
    public static JobEnqueueItem Create<TService>(
        Expression<Func<TService, CancellationToken, IProgress<decimal>, ValueTask>> work,
        JobSubmission? submission = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        return new JobEnqueueItem(typeof(TService), work, submission);
    }
}
