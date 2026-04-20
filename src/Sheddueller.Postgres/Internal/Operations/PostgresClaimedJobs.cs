namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

using Sheddueller.Storage;

internal static class PostgresClaimedTasks
{
    public static async ValueTask<IReadOnlyList<string>?> TryLockCurrentClaimGroupsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        string nodeId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var task = await TryReadCurrentClaimForFailureAsync(context, connection, transaction, taskId, nodeId, leaseToken, cancellationToken)
          .ConfigureAwait(false);
        return task?.GroupKeys;
    }

    public static async ValueTask<PostgresClaimedTask?> TryReadCurrentClaimForFailureAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        string nodeId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              task.task_id,
              task.attempt_count,
              task.max_attempts,
              task.retry_backoff_kind,
              task.retry_base_delay_ms,
              task.retry_max_delay_ms
          from {context.Names.Tasks} task
          where task.task_id = @task_id
            and task.state = 'Claimed'
            and task.claimed_by_node_id = @node_id
            and task.lease_token = @lease_token
            and task.lease_expires_at_utc > transaction_timestamp()
          for update;
          """;
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("node_id", nodeId);
        command.Parameters.AddWithValue("lease_token", leaseToken);

        PostgresClaimedTask? task = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                task = PostgresReaders.ReadPostgresClaimedTask(reader, []);
            }
        }

        if (task is null)
        {
            return null;
        }

        var groupKeys = await PostgresTaskGroups.ReadTaskGroupKeysAsync(context, connection, transaction, task.TaskId, cancellationToken)
          .ConfigureAwait(false);
        return task with { GroupKeys = groupKeys };
    }

    public static async ValueTask<IReadOnlyList<PostgresClaimedTask>> ReadExpiredClaimsAsync(
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
              task.task_id,
              task.attempt_count,
              task.max_attempts,
              task.retry_backoff_kind,
              task.retry_base_delay_ms,
              task.retry_max_delay_ms
          from {context.Names.Tasks} task
          where task.state = 'Claimed'
            and task.lease_expires_at_utc <= transaction_timestamp()
          order by task.lease_expires_at_utc asc, task.enqueue_sequence asc
          for update skip locked;
          """;

        var tasks = new List<PostgresClaimedTask>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(PostgresReaders.ReadPostgresClaimedTask(reader, []));
            }
        }

        for (var i = 0; i < tasks.Count; i++)
        {
            var groupKeys = await PostgresTaskGroups.ReadTaskGroupKeysAsync(context, connection, transaction, tasks[i].TaskId, cancellationToken)
              .ConfigureAwait(false);
            tasks[i] = tasks[i] with { GroupKeys = groupKeys };
        }

        return tasks;
    }

    public static async ValueTask ApplyFailedAttemptAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgresClaimedTask task,
        TaskFailureInfo failure,
        CancellationToken cancellationToken)
    {
        var retriesRemain = task.AttemptCount < task.MaxAttempts;
        var notBeforeExpression = retriesRemain ? "transaction_timestamp() + @retry_delay" : "null";
        await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.Tasks}
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
          where task_id = @task_id;
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", task.TaskId);
              command.Parameters.AddWithValue("state", retriesRemain ? "Queued" : "Failed");
              command.Parameters.AddWithValue("failure_type_name", failure.ExceptionType);
              command.Parameters.AddWithValue("failure_message", failure.Message);
              command.Parameters.AddWithValue("failure_stack_trace", PostgresOperationContext.ToDbValue(failure.StackTrace));

              if (retriesRemain)
              {
                  command.Parameters.AddWithValue("retry_delay", PostgresRetryPolicies.CalculateBackoff(task));
              }
          },
          cancellationToken)
          .ConfigureAwait(false);
    }
}
