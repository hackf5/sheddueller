namespace Sheddueller.Postgres.Internal.Operations;

internal static class DeleteRecurringScheduleOperation
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
          delete from {context.Names.RecurringSchedules}
          where schedule_key = @schedule_key;
          """,
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey),
          cancellationToken)
          .ConfigureAwait(false);

        return updated == 1;
    }
}
