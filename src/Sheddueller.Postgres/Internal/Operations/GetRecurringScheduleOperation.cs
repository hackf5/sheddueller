namespace Sheddueller.Postgres.Internal.Operations;

internal static class GetRecurringScheduleOperation
{
    public static async ValueTask<RecurringScheduleInfo?> ExecuteAsync(
        PostgresOperationContext context,
        string scheduleKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var schedules = await PostgresSchedules.ReadSchedulesAsync(
          context,
          connection,
          "where schedule.schedule_key = @schedule_key",
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey),
          cancellationToken)
          .ConfigureAwait(false);

        return schedules.SingleOrDefault();
    }
}
