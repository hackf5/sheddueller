namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class RenewJobLeaseOperation
{
    public static async ValueTask<bool> ExecuteAsync(
        PostgresOperationContext context,
        RenewLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var leaseDuration = request.LeaseExpiresAtUtc - request.HeartbeatAtUtc;
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Lease expiry must be after the heartbeat timestamp.", nameof(request));
        }

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var updated = await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.Jobs}
          set last_heartbeat_at_utc = transaction_timestamp(),
              lease_expires_at_utc = transaction_timestamp() + @lease_duration
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
              command.Parameters.AddWithValue("lease_duration", leaseDuration);
          },
          cancellationToken)
          .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return updated == 1;
    }
}
