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
        var retry = PostgresRetryPolicies.Normalize(request.RetryPolicy);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await PostgresSchedules.ReadScheduleDefinitionForUpdateAsync(context, connection, transaction, request.ScheduleKey, cancellationToken)
          .ConfigureAwait(false);

        if (existing is null)
        {
            var nextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(
              request.CronExpression,
              await PostgresOperationContext.ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
            await PostgresSchedules.InsertScheduleAsync(context, connection, transaction, request, retry, nextFireAtUtc, cancellationToken)
              .ConfigureAwait(false);
            await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecurringScheduleUpsertResult.Created;
        }

        if (existing.EqualsRequest(request))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecurringScheduleUpsertResult.Unchanged;
        }

        DateTimeOffset? updatedNextFireAtUtc = existing.IsPaused
          ? null
          : CronSchedule.GetNextOccurrenceAfter(
            request.CronExpression,
            await PostgresOperationContext.ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
        await PostgresSchedules.UpdateScheduleAsync(context, connection, transaction, request, retry, updatedNextFireAtUtc, cancellationToken)
          .ConfigureAwait(false);
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RecurringScheduleUpsertResult.Updated;
    }
}
