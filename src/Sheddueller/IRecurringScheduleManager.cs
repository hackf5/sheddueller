namespace Sheddueller;

using System.Linq.Expressions;

/// <summary>
/// Manages recurring schedules that materialize ordinary Sheddueller jobs.
/// </summary>
/// <remarks>
/// Schedule definitions are declarative and keyed. Calling <c>CreateOrUpdateAsync</c> during application
/// startup is the intended way to reconcile desired schedules with storage.
/// </remarks>
public interface IRecurringScheduleManager
{
    /// <summary>
    /// Creates or replaces a Task-returning recurring schedule definition.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a ValueTask-returning recurring schedule definition.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a Task-returning recurring schedule definition with scheduler-supplied progress reporting.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, IProgress<decimal>, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a ValueTask-returning recurring schedule definition with scheduler-supplied progress reporting.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
        string scheduleKey,
        string cronExpression,
        Expression<Func<CancellationToken, IProgress<decimal>, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a Task-returning recurring schedule definition.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when an occurrence runs.</typeparam>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a ValueTask-returning recurring schedule definition.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when an occurrence runs.</typeparam>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a Task-returning recurring schedule definition with scheduler-supplied progress reporting.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when an occurrence runs.</typeparam>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, IProgress<decimal>, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a ValueTask-returning recurring schedule definition with scheduler-supplied progress reporting.
    /// </summary>
    /// <typeparam name="TService">The service type resolved from dependency injection when an occurrence runs.</typeparam>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cronExpression">A standard five-field cron expression evaluated in UTC.</param>
    /// <param name="work">The method-call expression to materialize when an occurrence is due.</param>
    /// <param name="options">Options applied to jobs created by the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>Whether the schedule was created, changed, or already matched the submitted definition.</returns>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, IProgress<decimal>, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually triggers a recurring schedule by cloning its current stored template into one queued job.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>The trigger outcome, including the created job id when a job was enqueued.</returns>
    ValueTask<RecurringScheduleTriggerResult> TriggerAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a recurring schedule definition.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns><see langword="true"/> when an existing schedule was deleted.</returns>
    ValueTask<bool> DeleteAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a recurring schedule.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns><see langword="true"/> when an existing schedule was paused.</returns>
    ValueTask<bool> PauseAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a recurring schedule.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns><see langword="true"/> when an existing schedule was resumed.</returns>
    ValueTask<bool> ResumeAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a recurring schedule, if it exists.
    /// </summary>
    /// <param name="scheduleKey">The stable unique key for the schedule.</param>
    /// <param name="cancellationToken">A token for canceling the storage operation.</param>
    /// <returns>The current schedule definition, or <see langword="null"/> when the key is unknown.</returns>
    ValueTask<RecurringScheduleInfo?> GetAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recurring schedules.
    /// </summary>
    /// <param name="cancellationToken">A token for canceling enumeration.</param>
    /// <returns>The current schedule definitions in provider-defined order.</returns>
    IAsyncEnumerable<RecurringScheduleInfo> ListAsync(
        CancellationToken cancellationToken = default);
}
