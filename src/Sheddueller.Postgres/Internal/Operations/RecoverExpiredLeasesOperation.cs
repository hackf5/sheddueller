namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class RecoverExpiredLeasesOperation
{
    public static async ValueTask<int> ExecuteAsync(
        PostgresOperationContext context,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var expiredTasks = await PostgresClaimedTasks.ReadExpiredClaimsAsync(context, connection, transaction, cancellationToken)
          .ConfigureAwait(false);
        foreach (var task in expiredTasks)
        {
            await PostgresTaskGroups.DecrementGroupsAsync(context, connection, transaction, task.GroupKeys, cancellationToken).ConfigureAwait(false);
            await PostgresClaimedTasks.ApplyFailedAttemptAsync(
              context,
              connection,
              transaction,
              task,
              new TaskFailureInfo("Sheddueller.LeaseExpired", "The task lease expired before the owning node renewed it.", null),
              cancellationToken)
              .ConfigureAwait(false);
            await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendDashboardJobEventRequest(task.TaskId, DashboardJobEventKind.AttemptFailed, task.AttemptCount, Message: "The task lease expired before the owning node renewed it."),
              cancellationToken)
              .ConfigureAwait(false);
            await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendDashboardJobEventRequest(
                task.TaskId,
                DashboardJobEventKind.Lifecycle,
                task.AttemptCount,
                Message: task.AttemptCount < task.MaxAttempts ? "Retry scheduled" : "Failed"),
              cancellationToken)
              .ConfigureAwait(false);
        }

        if (expiredTasks.Count > 0)
        {
            await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expiredTasks.Count;
    }
}
