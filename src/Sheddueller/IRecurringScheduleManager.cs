namespace Sheddueller;

using System.Linq.Expressions;

/// <summary>
/// Manages recurring schedules that materialize ordinary Sheddueller jobs.
/// </summary>
public interface IRecurringScheduleManager
{
    /// <summary>
    /// Creates or replaces a Task-returning recurring schedule definition.
    /// </summary>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, Task>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a ValueTask-returning recurring schedule definition.
    /// </summary>
    ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
        string scheduleKey,
        string cronExpression,
        Expression<Func<TService, CancellationToken, ValueTask>> work,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a recurring schedule definition.
    /// </summary>
    ValueTask<bool> DeleteAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a recurring schedule.
    /// </summary>
    ValueTask<bool> PauseAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a recurring schedule.
    /// </summary>
    ValueTask<bool> ResumeAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a recurring schedule, if it exists.
    /// </summary>
    ValueTask<RecurringScheduleInfo?> GetAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recurring schedules.
    /// </summary>
    IAsyncEnumerable<RecurringScheduleInfo> ListAsync(
        CancellationToken cancellationToken = default);
}
