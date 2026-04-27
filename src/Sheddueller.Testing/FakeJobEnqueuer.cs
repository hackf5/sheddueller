namespace Sheddueller.Testing;

using System.Linq.Expressions;

using Sheddueller.Enqueueing;
using Sheddueller.Serialization;

/// <summary>
/// Test double for recording and matching jobs submitted through <see cref="IJobEnqueuer"/>.
/// </summary>
public sealed class FakeJobEnqueuer : IJobEnqueuer
{
    private readonly Lock _syncRoot = new();
    private readonly IJobPayloadSerializer _serializer;
    private readonly List<FakeEnqueuedJob> _jobs = [];
    private long _nextEnqueueSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeJobEnqueuer"/> class.
    /// </summary>
    public FakeJobEnqueuer()
      : this(new SystemTextJsonJobPayloadSerializer())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeJobEnqueuer"/> class.
    /// </summary>
    /// <param name="serializer">The serializer used to compare captured job arguments.</param>
    public FakeJobEnqueuer(IJobPayloadSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        this._serializer = serializer;
    }

    /// <summary>
    /// Gets a snapshot of jobs recorded by this fake.
    /// </summary>
    public IReadOnlyList<FakeEnqueuedJob> EnqueuedJobs
    {
        get
        {
            lock (this._syncRoot)
            {
                return Array.AsReadOnly([.. this._jobs]);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync(
      Expression<Func<CancellationToken, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync(
      Expression<Func<CancellationToken, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync(
      Expression<Func<CancellationToken, IProgress<decimal>, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync(
      Expression<Func<CancellationToken, IProgress<decimal>, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync<TService>(
      Expression<Func<TService, CancellationToken, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync<TService>(
      Expression<Func<TService, CancellationToken, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync<TService>(
      Expression<Func<TService, CancellationToken, IProgress<decimal>, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync<TService>(
      Expression<Func<TService, CancellationToken, IProgress<decimal>, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
    {
        var preparedJob = await this.PrepareJobAsync(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);

        lock (this._syncRoot)
        {
            var recordedJob = this.CreateRecordedJob(preparedJob, batchId: null, batchIndex: null);
            this._jobs.Add(recordedJob);

            return recordedJob.JobId;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Guid>> EnqueueManyAsync(
      IReadOnlyList<JobEnqueueItem> jobs,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        cancellationToken.ThrowIfCancellationRequested();

        if (jobs.Count == 0)
        {
            return [];
        }

        var jobSnapshot = jobs.ToArray();
        var preparedJobs = new PreparedJob[jobSnapshot.Length];

        for (var i = 0; i < jobSnapshot.Length; i++)
        {
            var job = jobSnapshot[i];
            ArgumentNullException.ThrowIfNull(job, nameof(jobs));

            preparedJobs[i] = await this
                .PrepareJobAsync(JobExpressionParser.Parse(job.ServiceType, job.Work), job.Submission, cancellationToken)
                .ConfigureAwait(false);
        }

        lock (this._syncRoot)
        {
            var batchId = Guid.NewGuid();
            var jobIds = new Guid[preparedJobs.Length];

            for (var i = 0; i < preparedJobs.Length; i++)
            {
                var recordedJob = this.CreateRecordedJob(preparedJobs[i], batchId, i);
                this._jobs.Add(recordedJob);
                jobIds[i] = recordedJob.JobId;
            }

            return jobIds;
        }
    }

    /// <summary>
    /// Finds recorded jobs that match a Task-returning job method call.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync(
      Expression<Func<CancellationToken, Task>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a ValueTask-returning job method call.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync(
      Expression<Func<CancellationToken, ValueTask>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a Task-returning job method call with scheduler-supplied progress reporting.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync(
      Expression<Func<CancellationToken, IProgress<decimal>, Task>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a ValueTask-returning job method call with scheduler-supplied progress reporting.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync(
      Expression<Func<CancellationToken, IProgress<decimal>, ValueTask>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a Task-returning service method call.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync<TService>(
      Expression<Func<TService, CancellationToken, Task>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a ValueTask-returning service method call.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync<TService>(
      Expression<Func<TService, CancellationToken, ValueTask>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a Task-returning service method call with scheduler-supplied progress reporting.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync<TService>(
      Expression<Func<TService, CancellationToken, IProgress<decimal>, Task>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds recorded jobs that match a ValueTask-returning service method call with scheduler-supplied progress reporting.
    /// </summary>
    public async ValueTask<FakeJobMatch> MatchAsync<TService>(
      Expression<Func<TService, CancellationToken, IProgress<decimal>, ValueTask>> work,
      CancellationToken cancellationToken = default)
      => await this.MatchCoreAsync(JobExpressionParser.Parse(work), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Removes all recorded jobs and resets enqueue sequence numbering.
    /// </summary>
    public void Clear()
    {
        lock (this._syncRoot)
        {
            this._jobs.Clear();
            this._nextEnqueueSequence = 0;
        }
    }

    private async ValueTask<FakeJobMatch> MatchCoreAsync(
      ParsedJob parsedJob,
      CancellationToken cancellationToken)
    {
        var serializedArguments = await this
            .SerializeArgumentsAsync(parsedJob, cancellationToken)
            .ConfigureAwait(false);
        FakeEnqueuedJob[] jobSnapshot;

        lock (this._syncRoot)
        {
            jobSnapshot = [.. this._jobs];
        }

        var matchedJobs = jobSnapshot
            .Where(job => Matches(job, parsedJob, serializedArguments))
            .ToArray();

        return new FakeJobMatch(matchedJobs);
    }

    private async ValueTask<PreparedJob> PrepareJobAsync(
      ParsedJob parsedJob,
      JobSubmission? submission,
      CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSubmission = NormalizeSubmission(submission);
        var serializedArguments = await this
            .SerializeArgumentsAsync(parsedJob, cancellationToken)
            .ConfigureAwait(false);

        return new PreparedJob(Guid.NewGuid(), parsedJob, serializedArguments, normalizedSubmission);
    }

    private async ValueTask<SerializedJobPayload> SerializeArgumentsAsync(
      ParsedJob parsedJob,
      CancellationToken cancellationToken)
    {
        var serializedArguments = await this._serializer
            .SerializeAsync(parsedJob.SerializableArguments, parsedJob.SerializableParameterTypes, cancellationToken)
            .ConfigureAwait(false);

        return FakeEnqueuedJob.ClonePayload(serializedArguments);
    }

    private FakeEnqueuedJob CreateRecordedJob(
      PreparedJob preparedJob,
      Guid? batchId,
      int? batchIndex)
      => new(
          preparedJob.JobId,
          this._nextEnqueueSequence++,
          batchId,
          batchIndex,
          preparedJob.ParsedJob.ServiceType,
          preparedJob.ParsedJob.MethodName,
          [.. preparedJob.ParsedJob.MethodParameterTypeNames.Select(TypeNameFormatter.Resolve)],
          preparedJob.ParsedJob.MethodParameterBindings,
          preparedJob.ParsedJob.InvocationTargetKind,
          preparedJob.ParsedJob.SerializableParameterTypes,
          preparedJob.ParsedJob.SerializableArguments,
          preparedJob.SerializedArguments,
          preparedJob.Submission);

    private static bool Matches(
      FakeEnqueuedJob job,
      ParsedJob parsedJob,
      SerializedJobPayload serializedArguments)
      => job.ServiceType == parsedJob.ServiceType
        && string.Equals(job.MethodName, parsedJob.MethodName, StringComparison.Ordinal)
        && job.InvocationTargetKind == parsedJob.InvocationTargetKind
        && job.MethodParameterTypes.Select(TypeNameFormatter.Format).SequenceEqual(parsedJob.MethodParameterTypeNames, StringComparer.Ordinal)
        && job.MethodParameterBindings.SequenceEqual(parsedJob.MethodParameterBindings)
        && PayloadsEqual(job.StoredSerializedArguments, serializedArguments);

    private static bool PayloadsEqual(
      SerializedJobPayload left,
      SerializedJobPayload right)
      => string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
        && left.Data.SequenceEqual(right.Data);

    private static JobSubmission NormalizeSubmission(JobSubmission? submission)
    {
        SubmissionValidator.ValidateIdempotency(submission);

        var groups = SubmissionValidator.NormalizeConcurrencyGroupKeys(submission?.ConcurrencyGroupKeys);
        var tags = SubmissionValidator.NormalizeJobTags(submission?.Tags);
        var retryPolicy = NormalizeRetryPolicy(submission?.RetryPolicy);

        return new JobSubmission(
          submission?.Priority ?? 0,
          groups,
          submission?.NotBeforeUtc?.ToUniversalTime(),
          retryPolicy,
          tags,
          submission?.IdempotencyKind ?? JobIdempotencyKind.None);
    }

    private static RetryPolicy? NormalizeRetryPolicy(RetryPolicy? retryPolicy)
    {
        SubmissionValidator.ValidateRetryPolicy(retryPolicy);

        return retryPolicy is { MaxAttempts: > 1 } ? retryPolicy : null;
    }

    private sealed record PreparedJob(
      Guid JobId,
      ParsedJob ParsedJob,
      SerializedJobPayload SerializedArguments,
      JobSubmission Submission);
}
