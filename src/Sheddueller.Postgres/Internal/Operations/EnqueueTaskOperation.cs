namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;

using Sheddueller.Storage;

internal static class EnqueueTaskOperation
{
    public static async ValueTask<EnqueueTaskResult> ExecuteAsync(
        PostgresOperationContext context,
        EnqueueTaskRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          insert into {context.Names.Tasks} (
              task_id,
              state,
              priority,
              enqueued_at_utc,
              not_before_utc,
              service_type,
              method_name,
              method_parameter_types,
              serialized_arguments_content_type,
              serialized_arguments,
              attempt_count,
              max_attempts,
              retry_backoff_kind,
              retry_base_delay_ms,
              retry_max_delay_ms,
              source_schedule_key,
              scheduled_fire_at_utc)
          values (
              @task_id,
              'Queued',
              @priority,
              transaction_timestamp(),
              @not_before_utc,
              @service_type,
              @method_name,
              @method_parameter_types,
              @serialized_arguments_content_type,
              @serialized_arguments,
              0,
              @max_attempts,
              @retry_backoff_kind,
              @retry_base_delay_ms,
              @retry_max_delay_ms,
              @source_schedule_key,
              @scheduled_fire_at_utc)
          returning enqueue_sequence;
          """;
        command.Parameters.AddWithValue("task_id", request.TaskId);
        command.Parameters.AddWithValue("priority", request.Priority);
        command.Parameters.AddWithValue("not_before_utc", PostgresOperationContext.ToDbValue(request.NotBeforeUtc));
        command.Parameters.AddWithValue("service_type", request.ServiceType);
        command.Parameters.AddWithValue("method_name", request.MethodName);
        command.Parameters.AddWithValue("method_parameter_types", request.MethodParameterTypes.ToArray());
        command.Parameters.AddWithValue("serialized_arguments_content_type", request.SerializedArguments.ContentType);
        command.Parameters.AddWithValue("serialized_arguments", request.SerializedArguments.Data);
        command.Parameters.AddWithValue("max_attempts", request.MaxAttempts);
        command.Parameters.AddWithValue("retry_backoff_kind", PostgresOperationContext.ToDbValue(PostgresConversion.ToText(request.RetryBackoffKind)));
        command.Parameters.AddWithValue("retry_base_delay_ms", PostgresOperationContext.ToDbValue(PostgresConversion.ToMilliseconds(request.RetryBaseDelay)));
        command.Parameters.AddWithValue("retry_max_delay_ms", PostgresOperationContext.ToDbValue(PostgresConversion.ToMilliseconds(request.RetryMaxDelay)));
        command.Parameters.AddWithValue("source_schedule_key", PostgresOperationContext.ToDbValue(request.SourceScheduleKey));
        command.Parameters.AddWithValue("scheduled_fire_at_utc", PostgresOperationContext.ToDbValue(request.ScheduledFireAtUtc));

        var enqueueSequence = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await PostgresTaskGroups.ReplaceTaskGroupsAsync(context, connection, transaction, request.TaskId, request.ConcurrencyGroupKeys, cancellationToken)
          .ConfigureAwait(false);
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new EnqueueTaskResult(request.TaskId, enqueueSequence);
    }
}
