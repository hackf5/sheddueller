namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class RecoverExpiredLeasesOperation
{
    public static async ValueTask<int> ExecuteAsync(
        PostgresOperationContext context,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var expiredJobs = await PostgresClaimedJobs.ReadExpiredClaimsAsync(context, connection, transaction, cancellationToken)
          .ConfigureAwait(false);
        foreach (var job in expiredJobs)
        {
            await PostgresJobGroups.DecrementGroupsAsync(context, connection, transaction, job.GroupKeys, cancellationToken).ConfigureAwait(false);
            var lifecycleMessage = await PostgresClaimedJobs.ApplyFailedAttemptAsync(
              context,
              connection,
              transaction,
              job,
              new JobFailureInfo("Sheddueller.LeaseExpired", "The job lease expired before the owning node renewed it.", null),
              cancellationToken)
              .ConfigureAwait(false);
            await PostgresJobEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendJobEventRequest(job.JobId, JobEventKind.AttemptFailed, job.AttemptCount, Message: "The job lease expired before the owning node renewed it."),
              cancellationToken)
              .ConfigureAwait(false);
            await PostgresJobEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendJobEventRequest(
                job.JobId,
                JobEventKind.Lifecycle,
                job.AttemptCount,
                Message: lifecycleMessage),
              cancellationToken)
              .ConfigureAwait(false);
        }

        if (expiredJobs.Count > 0)
        {
            await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expiredJobs.Count;
    }
}
