namespace Sheddueller.Testing;

using System.Linq.Expressions;

using Sheddueller.Serialization;

/// <summary>
/// Async-context-aware job enqueuer for dependency-injected tests.
/// </summary>
public sealed class CapturingJobEnqueuer(IJobPayloadSerializer serializer) : IJobEnqueuer
{
    private readonly AsyncLocal<FakeJobEnqueuer?> _current = new();
    private readonly IJobPayloadSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

    /// <summary>
    /// Begins capturing jobs enqueued in the current async context.
    /// </summary>
    public JobEnqueuerCapture Capture()
    {
        if (this._current.Value is not null)
        {
            throw new InvalidOperationException("A job enqueuer capture is already active in the current async context.");
        }

        var fake = new FakeJobEnqueuer(this._serializer);
        this._current.Value = fake;

        return new JobEnqueuerCapture(fake, this);
    }

    /// <inheritdoc />
    public ValueTask<Guid> EnqueueAsync(
      Expression<Func<CancellationToken, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().EnqueueAsync(work, submission, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Guid> EnqueueAsync(
      Expression<Func<CancellationToken, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().EnqueueAsync(work, submission, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Guid> EnqueueAsync<TService>(
      Expression<Func<TService, CancellationToken, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().EnqueueAsync(work, submission, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Guid> EnqueueAsync<TService>(
      Expression<Func<TService, CancellationToken, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().EnqueueAsync(work, submission, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Guid>> EnqueueManyAsync(
      IReadOnlyList<JobEnqueueItem> jobs,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().EnqueueManyAsync(jobs, cancellationToken);

    internal void ClearCapture(FakeJobEnqueuer fake)
    {
        if (ReferenceEquals(this._current.Value, fake))
        {
            this._current.Value = null;
        }
    }

    private FakeJobEnqueuer CurrentOrDiscardingFake()
      => this._current.Value ?? new FakeJobEnqueuer(this._serializer);
}
