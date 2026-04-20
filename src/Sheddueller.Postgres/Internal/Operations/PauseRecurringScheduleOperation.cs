namespace Sheddueller.Postgres.Internal.Operations;

internal static class PauseRecurringScheduleOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        string scheduleKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var updated = await PostgresOperationContext.ExecuteCountAsync(
          connection,
          $"""
          update {context.Names.RecurringSchedules}
          set is_paused = true,
              next_fire_at_utc = null,
              updated_at_utc = transaction_timestamp()
          where schedule_key = @schedule_key;
          """,
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey),
          cancellationToken)
          .ConfigureAwait(false);

        return updated == 1;
    }
}
