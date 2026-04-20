namespace Sheddueller.Postgres.Internal.Operations;

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
        var updated = await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.Tasks}
          set state = 'Canceled',
              canceled_at_utc = transaction_timestamp()
          where task_id = @task_id
            and state = 'Queued';
          """,
          command => command.Parameters.AddWithValue("task_id", request.TaskId),
          cancellationToken)
          .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return updated == 1;
    }
}
