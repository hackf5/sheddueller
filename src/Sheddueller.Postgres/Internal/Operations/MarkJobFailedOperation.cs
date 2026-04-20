namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class MarkJobFailedOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        FailJobRequest request,
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
        await PostgresClaimedJobs.ApplyFailedAttemptAsync(context, connection, transaction, job, request.Failure, cancellationToken)
          .ConfigureAwait(false);
        await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
          context,
          connection,
          transaction,
          new AppendDashboardJobEventRequest(job.JobId, DashboardJobEventKind.AttemptFailed, job.AttemptCount, Message: request.Failure.Message),
          cancellationToken)
          .ConfigureAwait(false);
        await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
          context,
          connection,
          transaction,
          new AppendDashboardJobEventRequest(
            job.JobId,
            DashboardJobEventKind.Lifecycle,
            job.AttemptCount,
            Message: job.AttemptCount < job.MaxAttempts ? "Retry scheduled" : "Failed"),
          cancellationToken)
          .ConfigureAwait(false);
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
