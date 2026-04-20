namespace Sheddueller.Postgres.Internal.Operations;

using System.Data;

using Npgsql;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class TryClaimNextTaskOperation
{
    private const int ClaimCandidateLimit = 8;

    public static async ValueTask<ClaimTaskResult> ExecuteAsync(
        PostgresOperationContext context,
        ClaimTaskRequest request,
        CancellationToken cancellationToken)
    {
        var leaseDuration = request.LeaseExpiresAtUtc - request.ClaimedAtUtc;
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Lease expiry must be after the claimed timestamp.", nameof(request));
        }

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var candidates = await ReadClaimCandidatesAsync(context, connection, transaction, cancellationToken).ConfigureAwait(false);
        foreach (var taskId in candidates)
        {
            var groupKeys = await PostgresTaskGroups.ReadTaskGroupKeysAsync(context, connection, transaction, taskId, cancellationToken)
              .ConfigureAwait(false);
            await PostgresTaskGroups.EnsureGroupRowsAsync(context, connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false);

            if (!await PostgresTaskGroups.TryReserveGroupsAsync(context, connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var leaseToken = Guid.NewGuid();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
              $"""
              update {context.Names.Tasks}
              set state = 'Claimed',
                  attempt_count = attempt_count + 1,
                  claimed_by_node_id = @node_id,
                  claimed_at_utc = transaction_timestamp(),
                  lease_token = @lease_token,
                  lease_expires_at_utc = transaction_timestamp() + @lease_duration,
                  last_heartbeat_at_utc = null
              where task_id = @task_id
                and state = 'Queued'
              returning
                  task_id,
                  enqueue_sequence,
                  priority,
                  service_type,
                  method_name,
                  method_parameter_types,
                  serialized_arguments_content_type,
                  serialized_arguments,
                  attempt_count,
                  max_attempts,
                  lease_token,
                  lease_expires_at_utc,
                  retry_backoff_kind,
                  retry_base_delay_ms,
                  retry_max_delay_ms,
                  source_schedule_key,
                  scheduled_fire_at_utc;
              """;
            command.Parameters.AddWithValue("task_id", taskId);
            command.Parameters.AddWithValue("node_id", request.NodeId);
            command.Parameters.AddWithValue("lease_token", leaseToken);
            command.Parameters.AddWithValue("lease_duration", leaseDuration);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var claimed = PostgresReaders.ReadClaimedTask(reader, groupKeys);
            await reader.DisposeAsync().ConfigureAwait(false);
            await PostgresDashboardEvents.AppendAndNotifyInTransactionAsync(
              context,
              connection,
              transaction,
              new AppendDashboardJobEventRequest(claimed.TaskId, DashboardJobEventKind.AttemptStarted, claimed.AttemptCount, Message: "Attempt started"),
              cancellationToken)
              .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new ClaimTaskResult.Claimed(claimed);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ClaimTaskResult.NoTaskAvailable();
    }

    private static async ValueTask<IReadOnlyList<Guid>> ReadClaimCandidatesAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select task.task_id
          from {context.Names.Tasks} task
          where task.state = 'Queued'
            and (task.not_before_utc is null or task.not_before_utc <= transaction_timestamp())
            and not exists (
                select 1
                from {context.Names.TaskConcurrencyGroups} task_group
                join {context.Names.ConcurrencyGroups} concurrency_group on concurrency_group.group_key = task_group.group_key
                where task_group.task_id = task.task_id
                  and concurrency_group.in_use_count >= coalesce(concurrency_group.configured_limit, 1)
            )
          order by task.priority desc, task.enqueue_sequence asc
          for update of task skip locked
          limit @candidate_limit;
          """;
        command.Parameters.AddWithValue("candidate_limit", ClaimCandidateLimit);

        var taskIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            taskIds.Add(reader.GetGuid(0));
        }

        return taskIds;
    }
}
