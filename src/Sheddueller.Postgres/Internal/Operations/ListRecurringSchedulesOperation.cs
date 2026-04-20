namespace Sheddueller.Postgres.Internal.Operations;

internal static class ListRecurringSchedulesOperation
{
    public static async ValueTask<IReadOnlyList<RecurringScheduleInfo>> ExecuteAsync(
        PostgresOperationContext context,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await PostgresSchedules.ReadSchedulesAsync(context, connection, string.Empty, _ => { }, cancellationToken)
          .ConfigureAwait(false);
    }
}
