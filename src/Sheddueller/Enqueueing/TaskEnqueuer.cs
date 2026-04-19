namespace Sheddueller.Enqueueing;

using Microsoft.Extensions.Options;

using Sheddueller.DependencyInjection;
using Sheddueller.Runtime;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class TaskEnqueuer(
  ITaskStore store,
  ITaskPayloadSerializer serializer,
  IOptions<ShedduellerOptions> options,
  TimeProvider timeProvider,
  IShedduellerWakeSignal wakeSignal) : ITaskEnqueuer
{
    public ValueTask<Guid> EnqueueAsync<TService>(
      System.Linq.Expressions.Expression<Func<TService, CancellationToken, Task>> work,
      TaskSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.EnqueueCoreAsync(work, submission, cancellationToken);

    public ValueTask<Guid> EnqueueAsync<TService>(
      System.Linq.Expressions.Expression<Func<TService, CancellationToken, ValueTask>> work,
      TaskSubmission? submission = null,
      CancellationToken cancellationToken = default)
      => this.EnqueueCoreAsync(work, submission, cancellationToken);

    private async ValueTask<Guid> EnqueueCoreAsync<TService, TResult>(
      System.Linq.Expressions.Expression<Func<TService, CancellationToken, TResult>> work,
      TaskSubmission? submission,
      CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var parsedTask = TaskExpressionParser.Parse(work);
        var groups = SubmissionValidator.NormalizeConcurrencyGroupKeys(submission?.ConcurrencyGroupKeys);
        var retryPolicy = submission?.RetryPolicy ?? options.Value.DefaultRetryPolicy;
        var (maxAttempts, retryBackoffKind, retryBaseDelay, retryMaxDelay) = SubmissionValidator.NormalizeRetryPolicy(retryPolicy);
        var serializedArguments = await serializer
          .SerializeAsync(parsedTask.SerializableArguments, parsedTask.SerializableParameterTypes, cancellationToken)
          .ConfigureAwait(false);

        var taskId = Guid.NewGuid();
        var request = new EnqueueTaskRequest(
          taskId,
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
          retryMaxDelay);

        var result = await store.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
        wakeSignal.Notify();

        return result.TaskId;
    }
}
