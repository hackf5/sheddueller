namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class MarkTaskCompletedOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        CompleteTaskRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var groupKeys = await PostgresClaimedTasks.TryLockCurrentClaimGroupsAsync(
          context,
          connection,
          transaction,
          request.TaskId,
          request.NodeId,
          request.LeaseToken,
          cancellationToken)
          .ConfigureAwait(false);

        if (groupKeys is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await PostgresTaskGroups.DecrementGroupsAsync(context, connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false);
        var updated = await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.Tasks}
          set state = 'Completed',
              completed_at_utc = transaction_timestamp()
          where task_id = @task_id
            and state = 'Claimed'
            and claimed_by_node_id = @node_id
            and lease_token = @lease_token
            and lease_expires_at_utc > transaction_timestamp();
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", request.TaskId);
              command.Parameters.AddWithValue("node_id", request.NodeId);
              command.Parameters.AddWithValue("lease_token", request.LeaseToken);
          },
          cancellationToken)
          .ConfigureAwait(false);

        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }
}
