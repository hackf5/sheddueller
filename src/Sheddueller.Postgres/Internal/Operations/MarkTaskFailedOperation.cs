namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class MarkTaskFailedOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        FailTaskRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var task = await PostgresClaimedTasks.TryReadCurrentClaimForFailureAsync(
          context,
          connection,
          transaction,
          request.TaskId,
          request.NodeId,
          request.LeaseToken,
          cancellationToken)
          .ConfigureAwait(false);

        if (task is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await PostgresTaskGroups.DecrementGroupsAsync(context, connection, transaction, task.GroupKeys, cancellationToken).ConfigureAwait(false);
        await PostgresClaimedTasks.ApplyFailedAttemptAsync(context, connection, transaction, task, request.Failure, cancellationToken)
          .ConfigureAwait(false);
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
