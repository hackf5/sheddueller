namespace Sheddueller.Runtime;

using System.Linq.Expressions;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheddueller.Enqueueing;
using Sheddueller.Scheduling;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class RecurringScheduleManager(
    IJobStore store,
    IJobPayloadSerializer serializer,
    IOptions<ShedduellerOptions> options,
    TimeProvider timeProvider,
    IShedduellerWakeSignal wakeSignal,
    ILogger<RecurringScheduleManager> logger) : IRecurringScheduleManager
{
    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, IProgress<decimal>, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, IProgress<decimal>, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

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

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, IProgress<decimal>, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, IProgress<decimal>, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
      => this.CreateOrUpdateCoreAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    public async ValueTask<RecurringScheduleTriggerResult> TriggerAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);

        var result = await store.TriggerRecurringScheduleAsync(
          new TriggerRecurringScheduleRequest(scheduleKey, options.Value.DefaultRetryPolicy),
          cancellationToken)
          .ConfigureAwait(false);

        if (result.Status == RecurringScheduleTriggerStatus.Enqueued)
        {
            wakeSignal.Notify();
        }

        logger.RecurringScheduleTriggered(scheduleKey, result.Status.ToString());

        return result;
    }

    public async ValueTask<bool> DeleteAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        var deleted = await store.DeleteRecurringScheduleAsync(scheduleKey, cancellationToken).ConfigureAwait(false);
        logger.RecurringScheduleDeleted(scheduleKey, deleted);

        return deleted;
    }

    public async ValueTask<bool> PauseAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        var paused = await store.PauseRecurringScheduleAsync(scheduleKey, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        logger.RecurringSchedulePaused(scheduleKey, paused);

        return paused;
    }

    public async ValueTask<bool> ResumeAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        var resumed = await store.ResumeRecurringScheduleAsync(scheduleKey, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        logger.RecurringScheduleResumed(scheduleKey, resumed);

        return resumed;
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

        return await this.CreateOrUpdateCoreAsync(
          scheduleKey,
          cronExpression,
          JobExpressionParser.Parse(work),
          options,
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateCoreAsync<TService, TResult>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, IProgress<decimal>, TResult>> work,
        RecurringScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        CronSchedule.Validate(cronExpression);
        ArgumentNullException.ThrowIfNull(work);

        return await this.CreateOrUpdateCoreAsync(
          scheduleKey,
          cronExpression,
          JobExpressionParser.Parse(work),
          options,
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateCoreAsync<TResult>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, TResult>> work,
        RecurringScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        CronSchedule.Validate(cronExpression);
        ArgumentNullException.ThrowIfNull(work);

        return await this.CreateOrUpdateCoreAsync(
          scheduleKey,
          cronExpression,
          JobExpressionParser.Parse(work),
          options,
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateCoreAsync<TResult>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, IProgress<decimal>, TResult>> work,
        RecurringScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        SubmissionValidator.ValidateScheduleKey(scheduleKey);
        CronSchedule.Validate(cronExpression);
        ArgumentNullException.ThrowIfNull(work);

        return await this.CreateOrUpdateCoreAsync(
          scheduleKey,
          cronExpression,
          JobExpressionParser.Parse(work),
          options,
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateCoreAsync(
        string scheduleKey,
        string cronExpression,
        ParsedJob parsedTask,
        RecurringScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        var groups = SubmissionValidator.NormalizeConcurrencyGroupKeys(options?.ConcurrencyGroupKeys);
        var tags = SubmissionValidator.NormalizeJobTags(options?.Tags);
        SubmissionValidator.ValidateRetryPolicy(options?.RetryPolicy);

        var serializedArguments = await serializer
          .SerializeAsync(parsedTask.SerializableArguments, parsedTask.SerializableParameterTypes, cancellationToken)
          .ConfigureAwait(false);

        var request = new UpsertRecurringScheduleRequest(
          scheduleKey,
          cronExpression,
          TypeNameFormatter.Format(parsedTask.ServiceType),
          parsedTask.MethodName,
          parsedTask.MethodParameterTypeNames,
          serializedArguments,
          options?.Priority ?? 0,
          groups,
          options?.RetryPolicy,
          options?.OverlapMode ?? RecurringOverlapMode.Skip,
          timeProvider.GetUtcNow(),
          tags,
          parsedTask.InvocationTargetKind,
          parsedTask.MethodParameterBindings);

        var result = await store.CreateOrUpdateRecurringScheduleAsync(request, cancellationToken).ConfigureAwait(false);
        wakeSignal.Notify();
        logger.RecurringScheduleUpserted(scheduleKey, result.ToString());

        return result;
    }
}
