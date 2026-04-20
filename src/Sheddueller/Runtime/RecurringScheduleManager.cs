namespace Sheddueller.Runtime;

using System.Linq.Expressions;
using System.Runtime.CompilerServices;

using Sheddueller.Enqueueing;
using Sheddueller.Scheduling;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class RecurringScheduleManager(
    IJobStore store,
    IJobPayloadSerializer serializer,
    TimeProvider timeProvider,
    IShedduellerWakeSignal wakeSignal) : IRecurringScheduleManager
{
    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public ValueTask<bool> DeleteAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        return store.DeleteRecurringScheduleAsync(scheduleKey, cancellationToken);
    }

    public ValueTask<bool> PauseAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        return store.PauseRecurringScheduleAsync(scheduleKey, timeProvider.GetUtcNow(), cancellationToken);
    }

    public ValueTask<bool> ResumeAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        return store.ResumeRecurringScheduleAsync(scheduleKey, timeProvider.GetUtcNow(), cancellationToken);
    }

    public ValueTask<RecurringScheduleInfo?> GetAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        return store.GetRecurringScheduleAsync(scheduleKey, cancellationToken);
    }

    public async IAsyncEnumerable<RecurringScheduleInfo> ListAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var schedule in await store.ListRecurringSchedulesAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return schedule;
        }
    }

    private async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateCoreAsync<TService, TResult>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, TResult>> work,
        RecurringScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        CronSchedule.Validate(cronExpression);
        ArgumentNullException.ThrowIfNull(work);

        return await this.CreateOrUpdateParsedCoreAsync<TService>(
          scheduleKey,
          cronExpression,
          JobExpressionParser.Parse(work),
          options,
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateParsedCoreAsync<TService>(
        string scheduleKey,
        string cronExpression,
        ParsedJob parsedTask,
        RecurringScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        var groups = SubmissionValidator.NormalizeConcurrencyGroupKeys(options?.ConcurrencyGroupKeys);
        SubmissionValidator.ValidateRetryPolicy(options?.RetryPolicy);

        var serializedArguments = await serializer
          .SerializeAsync(parsedTask.SerializableArguments, parsedTask.SerializableParameterTypes, cancellationToken)
          .ConfigureAwait(false);

        var request = new UpsertRecurringScheduleRequest(
          scheduleKey,
          cronExpression,
          TypeNameFormatter.Format(typeof(TService)),
          parsedTask.MethodName,
          parsedTask.MethodParameterTypeNames,
          serializedArguments,
          options?.Priority ?? 0,
          groups,
          options?.RetryPolicy,
          options?.OverlapMode ?? RecurringOverlapMode.Skip,
          timeProvider.GetUtcNow());

        var result = await store.CreateOrUpdateRecurringScheduleAsync(request, cancellationToken).ConfigureAwait(false);
        wakeSignal.Notify();

        return result;
    }
}
