namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Scheduling;
using Sheddueller.Storage;

internal static class CreateOrUpdateRecurringScheduleOperation
{
    public static async ValueTask<RecurringScheduleUpsertResult> ExecuteAsync(
        PostgresOperationContext context,
        UpsertRecurringScheduleRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await PostgresSchedules.ReadScheduleDefinitionForUpdateAsync(context, connection, transaction, request.ScheduleKey, cancellationToken)
          .ConfigureAwait(false);
        var updateOptions = request.UpdateOptions ?? RecurringScheduleUpdateOptions.Default;

        if (existing is null)
        {
            var insertRetry = PostgresRetryPolicies.Normalize(request.RetryPolicy);
            var nextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(
              request.CronExpression,
              await PostgresOperationContext.ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
            await PostgresSchedules.InsertScheduleAsync(context, connection, transaction, request, insertRetry, nextFireAtUtc, cancellationToken)
              .ConfigureAwait(false);
            await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecurringScheduleUpsertResult.Created;
        }

        var effectiveRequest = request with
        {
            CronExpression = updateOptions.OverwriteCronExpression ? request.CronExpression : existing.CronExpression,
        };
        var effectiveIsPaused = !updateOptions.OverwritePausedState && existing.IsPaused;

        if (existing.EqualsRequest(effectiveRequest) && existing.IsPaused == effectiveIsPaused)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecurringScheduleUpsertResult.Unchanged;
        }

        var retry = PostgresRetryPolicies.Normalize(effectiveRequest.RetryPolicy);
        DateTimeOffset? updatedNextFireAtUtc = effectiveIsPaused
          ? null
          : CronSchedule.GetNextOccurrenceAfter(
            effectiveRequest.CronExpression,
            await PostgresOperationContext.ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
        await PostgresSchedules.UpdateScheduleAsync(context, connection, transaction, effectiveRequest, retry, effectiveIsPaused, updatedNextFireAtUtc, cancellationToken)
          .ConfigureAwait(false);
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RecurringScheduleUpsertResult.Updated;
    }
}
