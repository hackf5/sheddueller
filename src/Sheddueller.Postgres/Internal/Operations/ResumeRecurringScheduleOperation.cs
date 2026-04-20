namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Scheduling;

internal static class ResumeRecurringScheduleOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        string scheduleKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var schedule = await PostgresSchedules.ReadScheduleDefinitionForUpdateAsync(context, connection, transaction, scheduleKey, cancellationToken)
          .ConfigureAwait(false);
        if (schedule is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var nextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(
          schedule.CronExpression,
          await PostgresOperationContext.ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
        var updated = await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.RecurringSchedules}
          set is_paused = false,
              next_fire_at_utc = @next_fire_at_utc,
              updated_at_utc = transaction_timestamp()
          where schedule_key = @schedule_key;
          """,
          command =>
          {
              command.Parameters.AddWithValue("schedule_key", scheduleKey);
              command.Parameters.AddWithValue("next_fire_at_utc", nextFireAtUtc);
          },
          cancellationToken)
          .ConfigureAwait(false);

        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }
}
