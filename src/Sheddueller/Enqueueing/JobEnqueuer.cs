namespace Sheddueller.Enqueueing;

using Microsoft.Extensions.Options;

using Sheddueller.DependencyInjection;
using Sheddueller.Runtime;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class JobEnqueuer(
  IJobStore store,
  IJobPayloadSerializer serializer,
  IOptions<ShedduellerOptions> options,
  TimeProvider timeProvider,
  IShedduellerWakeSignal wakeSignal) : IJobEnqueuer
{
    public ValueTask<Guid> EnqueueAsync<TService>(
      System.Linq.Expressions.Expression<Func<TService, CancellationToken, Task>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.EnqueueCoreAsync(work, submission, cancellationToken);

    public ValueTask<Guid> EnqueueAsync<TService>(
      System.Linq.Expressions.Expression<Func<TService, CancellationToken, ValueTask>> work,
      JobSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.EnqueueCoreAsync(work, submission, cancellationToken);

    private async ValueTask<Guid> EnqueueCoreAsync<TService, TResult>(
      System.Linq.Expressions.Expression<Func<TService, CancellationToken, TResult>> work,
      JobSubmission? submission,
      CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        return await this.EnqueueParsedCoreAsync<TService>(JobExpressionParser.Parse(work), submission, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Guid> EnqueueParsedCoreAsync<TService>(
      ParsedJob parsedTask,
      JobSubmission? submission,
      CancellationToken cancellationToken)
    {
        var groups = SubmissionValidator.NormalizeConcurrencyGroupKeys(submission?.ConcurrencyGroupKeys);
        var tags = SubmissionValidator.NormalizeJobTags(submission?.Tags);
        var retryPolicy = submission?.RetryPolicy ?? options.Value.DefaultRetryPolicy;
        var (maxAttempts, retryBackoffKind, retryBaseDelay, retryMaxDelay) = SubmissionValidator.NormalizeRetryPolicy(retryPolicy);
        var serializedArguments = await serializer
          .SerializeAsync(parsedTask.SerializableArguments, parsedTask.SerializableParameterTypes, cancellationToken)
          .ConfigureAwait(false);

        var jobId = Guid.NewGuid();
        var request = new EnqueueJobRequest(
          jobId,
          submission?.Priority ?? 0,
          TypeNameFormatter.Format(typeof(TService)),
          parsedTask.MethodName,
          parsedTask.MethodParameterTypeNames,
          serializedArguments,
          groups,
          timeProvider.GetUtcNow(),
          submission?.NotBeforeUtc?.ToUniversalTime(),
          maxAttempts,
          retryBackoffKind,
          retryBaseDelay,
          retryMaxDelay,
          SourceScheduleKey: null,
          ScheduledFireAtUtc: null,
          Tags: tags);

        var result = await store.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
        wakeSignal.Notify();

        return result.JobId;
    }
}
