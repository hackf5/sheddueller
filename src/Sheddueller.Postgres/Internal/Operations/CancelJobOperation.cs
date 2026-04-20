namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class CancelTaskOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        CancelTaskRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          update {context.Names.Tasks}
          set state = 'Canceled',
              canceled_at_utc = transaction_timestamp()
          where task_id = @task_id
            and state = 'Queued'
          returning attempt_count;
          """;
        command.Parameters.AddWithValue("task_id", request.TaskId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendDashboardJobEventRequest(request.TaskId, DashboardJobEventKind.Lifecycle, Convert.ToInt32(result, CultureInfo.InvariantCulture), Message: "Canceled"),
              cancellationToken)
              .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result is not null;
    }
}
