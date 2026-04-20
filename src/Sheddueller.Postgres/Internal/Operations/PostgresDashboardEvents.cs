namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;
using System.Text.Json;

using Npgsql;

using NpgsqlTypes;

using Sheddueller.Dashboard;

internal static class PostgresDashboardEvents
{
    public static async ValueTask<DashboardJobEvent> AppendAsync(
        PostgresOperationContext context,
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var jobEvent = await AppendAndNotifyInTransactionAsync(context, connection, transaction, request, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return jobEvent;
    }

    public static async ValueTask<DashboardJobEvent> AppendAndNotifyInTransactionAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken)
    {
        var jobEvent = await AppendInTransactionAsync(context, connection, transaction, request, cancellationToken).ConfigureAwait(false);
        await NotifyAsync(context, connection, transaction, jobEvent, cancellationToken).ConfigureAwait(false);

        return jobEvent;
    }

    public static async ValueTask<DashboardJobEvent?> ReadEventAsync(
        PostgresOperationContext context,
        Guid taskId,
        long eventSequence,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select
              event_id,
              task_id,
              event_sequence,
              kind,
              occurred_at_utc,
              attempt_number,
              log_level,
              message,
              progress_percent,
              fields
          from {context.Names.DashboardEvents}
          where task_id = @task_id
            and event_sequence = @event_sequence;
          """;
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("event_sequence", eventSequence);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
          ? ReadEvent(reader)
          : null;
    }

    public static DashboardJobEvent ReadEvent(NpgsqlDataReader reader)
      => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetInt64(2),
        PostgresConversion.ToDashboardJobEventKind(reader.GetValue(3)),
        PostgresConversion.ToDateTimeOffset(reader.GetValue(4)),
        reader.GetInt32(5),
        PostgresConversion.ToJobLogLevel(reader.GetValue(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetDouble(8),
        reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(9)));

    private static async ValueTask<DashboardJobEvent> AppendInTransactionAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var eventSequence = await IncrementEventSequenceAsync(context, connection, transaction, request.TaskId, cancellationToken).ConfigureAwait(false);
        var eventId = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          insert into {context.Names.DashboardEvents} (
              task_id,
              event_sequence,
              event_id,
              kind,
              occurred_at_utc,
              attempt_number,
              log_level,
              message,
              progress_percent,
              fields)
          values (
              @task_id,
              @event_sequence,
              @event_id,
              @kind,
              transaction_timestamp(),
              @attempt_number,
              @log_level,
              @message,
              @progress_percent,
              @fields)
          returning occurred_at_utc;
          """;
        command.Parameters.AddWithValue("task_id", request.TaskId);
        command.Parameters.AddWithValue("event_sequence", eventSequence);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("kind", PostgresConversion.ToText(request.Kind));
        command.Parameters.AddWithValue("attempt_number", request.AttemptNumber);
        command.Parameters.AddWithValue("log_level", PostgresOperationContext.ToDbValue(request.LogLevel is null ? null : PostgresConversion.ToText(request.LogLevel.Value)));
        command.Parameters.AddWithValue("message", PostgresOperationContext.ToDbValue(request.Message));
        command.Parameters.AddWithValue("progress_percent", PostgresOperationContext.ToDbValue(request.ProgressPercent));

        if (request.Fields is null)
        {
            command.Parameters.AddWithValue("fields", DBNull.Value);
        }
        else
        {
            command.Parameters.Add("fields", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(request.Fields);
        }

        var occurredAtUtc = PostgresConversion.ToDateTimeOffset(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
          ?? throw new InvalidOperationException("PostgreSQL did not return a dashboard event timestamp."));

        return new DashboardJobEvent(
          eventId,
          request.TaskId,
          eventSequence,
          request.Kind,
          occurredAtUtc,
          request.AttemptNumber,
          request.LogLevel,
          request.Message,
          request.ProgressPercent,
          request.Fields);
    }

    private static async ValueTask<long> IncrementEventSequenceAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          update {context.Names.Tasks}
          set dashboard_event_sequence = dashboard_event_sequence + 1
          where task_id = @task_id
          returning dashboard_event_sequence;
          """;
        command.Parameters.AddWithValue("task_id", taskId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
          ?? throw new InvalidOperationException($"Task '{taskId}' does not exist.");
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async ValueTask NotifyAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken)
      => await PostgresOperationContext.ExecuteCountAsync(
        connection,
        transaction,
        "select pg_notify(@channel, @payload);",
        command =>
        {
            command.Parameters.AddWithValue("channel", PostgresNames.DashboardEventChannel);
            command.Parameters.AddWithValue("payload", $"{context.Options.SchemaName}|{jobEvent.TaskId:N}|{jobEvent.EventSequence}");
        },
        cancellationToken)
      .ConfigureAwait(false);

    private static void ValidateRequest(AppendDashboardJobEventRequest request)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Dashboard job event kind is not supported.");
        }

        if (request.AttemptNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.AttemptNumber, "Dashboard event attempt number cannot be negative.");
        }

        if (request.ProgressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.ProgressPercent, "Progress percent must be between 0 and 100.");
        }

        if (request.LogLevel is not null && !Enum.IsDefined(request.LogLevel.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.LogLevel, "Job log level is not supported.");
        }
    }
}
