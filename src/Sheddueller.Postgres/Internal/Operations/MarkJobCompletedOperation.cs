namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class MarkJobCompletedOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        CompleteJobRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var job = await PostgresClaimedJobs.TryReadCurrentClaimForFailureAsync(
          context,
          connection,
          transaction,
          request.JobId,
          request.NodeId,
          request.LeaseToken,
          cancellationToken)
          .ConfigureAwait(false);

        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await PostgresJobGroups.DecrementGroupsAsync(context, connection, transaction, job.GroupKeys, cancellationToken).ConfigureAwait(false);
        var updated = await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.Jobs}
          set state = 'Completed',
              completed_at_utc = transaction_timestamp()
          where job_id = @job_id
            and state = 'Claimed'
            and claimed_by_node_id = @node_id
            and lease_token = @lease_token
            and lease_expires_at_utc > transaction_timestamp();
          """,
          command =>
          {
              command.Parameters.AddWithValue("job_id", request.JobId);
              command.Parameters.AddWithValue("node_id", request.NodeId);
              command.Parameters.AddWithValue("lease_token", request.LeaseToken);
          },
          cancellationToken)
          .ConfigureAwait(false);

        if (updated == 1)
        {
            await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendDashboardJobEventRequest(job.JobId, DashboardJobEventKind.AttemptCompleted, job.AttemptCount, Message: "Attempt completed"),
              cancellationToken)
              .ConfigureAwait(false);
            await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendDashboardJobEventRequest(job.JobId, DashboardJobEventKind.Lifecycle, job.AttemptCount, Message: "Completed"),
              cancellationToken)
              .ConfigureAwait(false);
            await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }
}
