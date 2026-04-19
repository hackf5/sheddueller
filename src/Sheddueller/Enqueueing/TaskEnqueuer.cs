#pragma warning disable IDE0130

namespace Sheddueller;

internal sealed class TaskEnqueuer(
  ITaskStore store,
  ITaskPayloadSerializer serializer,
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
          timeProvider.GetUtcNow());

        var result = await store.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
        wakeSignal.Notify();

        return result.TaskId;
    }
}
