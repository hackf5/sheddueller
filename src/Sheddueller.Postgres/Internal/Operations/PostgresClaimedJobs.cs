namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

using Sheddueller.Storage;

internal static class PostgresClaimedJobs
{
    public static async ValueTask<IReadOnlyList<string>?> TryLockCurrentClaimGroupsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        string nodeId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var job = await TryReadCurrentClaimForFailureAsync(context, connection, transaction, jobId, nodeId, leaseToken, cancellationToken)
          .ConfigureAwait(false);
        return job?.GroupKeys;
    }

    public static async ValueTask<PostgresClaimedJob?> TryReadCurrentClaimForFailureAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        string nodeId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              job.job_id,
              job.attempt_count,
              job.max_attempts,
              job.retry_backoff_kind,
              job.retry_base_delay_ms,
              job.retry_max_delay_ms
          from {context.Names.Jobs} job
          where job.job_id = @job_id
            and job.state = 'Claimed'
            and job.claimed_by_node_id = @node_id
            and job.lease_token = @lease_token
            and job.lease_expires_at_utc > transaction_timestamp()
          for update;
          """;
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("node_id", nodeId);
        command.Parameters.AddWithValue("lease_token", leaseToken);

        PostgresClaimedJob? job = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                job = PostgresReaders.ReadPostgresClaimedJob(reader, []);
            }
        }

        if (job is null)
        {
            return null;
        }

        var groupKeys = await PostgresJobGroups.ReadJobGroupKeysAsync(context, connection, transaction, job.JobId, cancellationToken)
          .ConfigureAwait(false);
        return job with { GroupKeys = groupKeys };
    }

    public static async ValueTask<IReadOnlyList<PostgresClaimedJob>> ReadExpiredClaimsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              job.job_id,
              job.attempt_count,
              job.max_attempts,
              job.retry_backoff_kind,
              job.retry_base_delay_ms,
              job.retry_max_delay_ms
          from {context.Names.Jobs} job
          where job.state = 'Claimed'
            and job.lease_expires_at_utc <= transaction_timestamp()
          order by job.lease_expires_at_utc asc, job.enqueue_sequence asc
          for update skip locked;
          """;

        var jobs = new List<PostgresClaimedJob>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                jobs.Add(PostgresReaders.ReadPostgresClaimedJob(reader, []));
            }
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            var groupKeys = await PostgresJobGroups.ReadJobGroupKeysAsync(context, connection, transaction, jobs[i].JobId, cancellationToken)
              .ConfigureAwait(false);
            jobs[i] = jobs[i] with { GroupKeys = groupKeys };
        }

        return jobs;
    }

    public static async ValueTask ApplyFailedAttemptAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgresClaimedJob job,
        JobFailureInfo failure,
        CancellationToken cancellationToken)
    {
        var retriesRemain = job.AttemptCount < job.MaxAttempts;
        var notBeforeExpression = retriesRemain ? "transaction_timestamp() + @retry_delay" : "null";
        await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.Jobs}
          set state = @state,
              failed_at_utc = transaction_timestamp(),
              failure_type_name = @failure_type_name,
              failure_message = @failure_message,
              failure_stack_trace = @failure_stack_trace,
              not_before_utc = {notBeforeExpression},
              claimed_by_node_id = null,
              claimed_at_utc = null,
              lease_token = null,
              lease_expires_at_utc = null,
              last_heartbeat_at_utc = null
          where job_id = @job_id;
          """,
          command =>
          {
              command.Parameters.AddWithValue("job_id", job.JobId);
              command.Parameters.AddWithValue("state", retriesRemain ? "Queued" : "Failed");
              command.Parameters.AddWithValue("failure_type_name", failure.ExceptionType);
              command.Parameters.AddWithValue("failure_message", failure.Message);
              command.Parameters.AddWithValue("failure_stack_trace", PostgresOperationContext.ToDbValue(failure.StackTrace));

              if (retriesRemain)
              {
                  command.Parameters.AddWithValue("retry_delay", PostgresRetryPolicies.CalculateBackoff(job));
              }
          },
          cancellationToken)
          .ConfigureAwait(false);
    }
}
