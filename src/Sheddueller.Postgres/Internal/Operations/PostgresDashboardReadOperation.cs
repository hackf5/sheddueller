namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;
using System.Runtime.CompilerServices;

using Npgsql;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class PostgresDashboardReadOperation
{
    public static async ValueTask<DashboardJobOverview> GetOverviewAsync(
        PostgresOperationContext context,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var counts = await ReadStateCountsAsync(context, connection, cancellationToken).ConfigureAwait(false);
        var running = await ReadSummaryPageAsync(context, connection, "where state = 'Claimed' order by claimed_at_utc desc nulls last, enqueue_sequence desc limit 10", static _ => { }, cancellationToken)
          .ConfigureAwait(false);
        var recentlyFailed = await ReadSummaryPageAsync(context, connection, "where state = 'Failed' order by failed_at_utc desc nulls last, enqueue_sequence desc limit 10", static _ => { }, cancellationToken)
          .ConfigureAwait(false);
        var queuedPage = await SearchJobsAsync(context, new DashboardJobQuery(State: TaskState.Queued, PageSize: 100), cancellationToken).ConfigureAwait(false);

        return new DashboardJobOverview(
          counts,
          running,
          recentlyFailed,
          [.. queuedPage.Jobs.Where(job => job.QueuePosition?.Kind == DashboardQueuePositionKind.Claimable).Take(10)],
          [.. queuedPage.Jobs.Where(job => job.QueuePosition?.Kind == DashboardQueuePositionKind.Delayed).Take(10)],
          [.. queuedPage.Jobs.Where(job => job.QueuePosition?.Kind == DashboardQueuePositionKind.RetryWaiting).Take(10)]);
    }

    public static async ValueTask<DashboardJobPage> SearchJobsAsync(
        PostgresOperationContext context,
        DashboardJobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateJobQuery(query);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var afterSequence = DecodeContinuationToken(query.ContinuationToken);
        var conditions = new List<string>();
        void configure(NpgsqlCommand command)
        {
            if (query.TaskId is { } taskId)
            {
                conditions.Add("task.task_id = @task_id");
                command.Parameters.AddWithValue("task_id", taskId);
            }

            if (query.State is { } state)
            {
                conditions.Add("task.state = @state");
                command.Parameters.AddWithValue("state", PostgresConversion.ToText(state));
            }

            if (query.ServiceType is not null)
            {
                conditions.Add("task.service_type = @service_type");
                command.Parameters.AddWithValue("service_type", query.ServiceType);
            }

            if (query.MethodName is not null)
            {
                conditions.Add("task.method_name = @method_name");
                command.Parameters.AddWithValue("method_name", query.MethodName);
            }

            if (query.Tag is { } tag)
            {
                conditions.Add($"exists (select 1 from {context.Names.TaskTags} tag where tag.task_id = task.task_id and tag.name = @tag_name and tag.value = @tag_value)");
                command.Parameters.AddWithValue("tag_name", tag.Name.Trim());
                command.Parameters.AddWithValue("tag_value", tag.Value.Trim());
            }

            if (query.SourceScheduleKey is not null)
            {
                conditions.Add("task.source_schedule_key = @source_schedule_key");
                command.Parameters.AddWithValue("source_schedule_key", query.SourceScheduleKey);
            }

            if (query.EnqueuedFromUtc is { } enqueuedFromUtc)
            {
                conditions.Add("task.enqueued_at_utc >= @enqueued_from_utc");
                command.Parameters.AddWithValue("enqueued_from_utc", enqueuedFromUtc);
            }

            if (query.EnqueuedToUtc is { } enqueuedToUtc)
            {
                conditions.Add("task.enqueued_at_utc <= @enqueued_to_utc");
                command.Parameters.AddWithValue("enqueued_to_utc", enqueuedToUtc);
            }

            if (query.TerminalFromUtc is { } terminalFromUtc)
            {
                conditions.Add("coalesce(task.completed_at_utc, task.failed_at_utc, task.canceled_at_utc) >= @terminal_from_utc");
                command.Parameters.AddWithValue("terminal_from_utc", terminalFromUtc);
            }

            if (query.TerminalToUtc is { } terminalToUtc)
            {
                conditions.Add("coalesce(task.completed_at_utc, task.failed_at_utc, task.canceled_at_utc) <= @terminal_to_utc");
                command.Parameters.AddWithValue("terminal_to_utc", terminalToUtc);
            }

            if (afterSequence is { } sequence)
            {
                conditions.Add("task.enqueue_sequence < @after_sequence");
                command.Parameters.AddWithValue("after_sequence", sequence);
            }

            command.Parameters.AddWithValue("limit", query.PageSize + 1);
        }

        await using var command = connection.CreateCommand();
        configure(command);
        var whereClause = conditions.Count == 0 ? string.Empty : $"where {string.Join(" and ", conditions)}";
        command.CommandText =
          $"""
          {TaskSelectSql(context)}
          {whereClause}
          order by task.enqueue_sequence desc
          limit @limit;
          """;

        var rows = await ReadRowsAsync(command, cancellationToken).ConfigureAwait(false);
        var pageRows = rows.Take(query.PageSize).ToArray();
        var jobs = new List<DashboardJobSummary>(pageRows.Length);
        foreach (var row in pageRows)
        {
            jobs.Add(await CreateSummaryAsync(context, connection, row, cancellationToken).ConfigureAwait(false));
        }

        var continuationToken = rows.Count > query.PageSize
          ? pageRows[^1].EnqueueSequence.ToString(CultureInfo.InvariantCulture)
          : null;

        return new DashboardJobPage(jobs, continuationToken);
    }

    public static async ValueTask<DashboardJobDetail?> GetJobAsync(
        PostgresOperationContext context,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await ReadTaskRowAsync(context, connection, taskId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var events = await ReadEventPageAsync(context, connection, taskId, afterEventSequence: null, limit: 100, cancellationToken).ConfigureAwait(false);
        return new DashboardJobDetail(
          await CreateSummaryAsync(context, connection, row, cancellationToken).ConfigureAwait(false),
          row.ClaimedAtUtc,
          row.ClaimedByNodeId,
          row.LeaseExpiresAtUtc,
          row.ScheduledFireAtUtc,
          events);
    }

    public static async ValueTask<DashboardQueuePosition> GetQueuePositionAsync(
        PostgresOperationContext context,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await ReadTaskRowAsync(context, connection, taskId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.NotFound, Position: null, "Job was not found.");
        }

        if (row.State == TaskState.Canceled)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Canceled, Position: null, "Job was canceled.");
        }

        if (row.State is TaskState.Completed or TaskState.Failed)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Terminal, Position: null, "Job is terminal.");
        }

        if (row.State == TaskState.Claimed)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Claimed, Position: null, "Job is currently claimed.");
        }

        var now = await ReadCurrentTimestampAsync(connection, cancellationToken).ConfigureAwait(false);
        if (row.NotBeforeUtc is { } notBeforeUtc && notBeforeUtc > now)
        {
            return row.FailedAtUtc is null
              ? new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Delayed, Position: null, $"Job is delayed until {notBeforeUtc:O}.")
              : new DashboardQueuePosition(taskId, DashboardQueuePositionKind.RetryWaiting, Position: null, $"Job is waiting to retry until {notBeforeUtc:O}.");
        }

        var position = await ReadClaimablePositionAsync(context, connection, taskId, cancellationToken).ConfigureAwait(false);
        if (position is not null)
        {
            return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.Claimable, position, "Job is currently claimable.");
        }

        return new DashboardQueuePosition(taskId, DashboardQueuePositionKind.BlockedByConcurrency, Position: null, "Job is blocked by concurrency group limits.");
    }

    public static async IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        PostgresOperationContext context,
        Guid taskId,
        DashboardEventQuery? query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        query ??= new DashboardEventQuery();
        ValidateEventQuery(query);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var events = await ReadEventPageAsync(context, connection, taskId, query.AfterEventSequence, query.Limit, cancellationToken).ConfigureAwait(false);
        foreach (var jobEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return jobEvent;
        }
    }

    public static async ValueTask<int> CleanupAsync(
        PostgresOperationContext context,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "Dashboard event retention must be positive.");
        }

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await PostgresOperationContext.ExecuteCountAsync(
          connection,
          $"""
          delete from {context.Names.DashboardEvents} event
          using {context.Names.Tasks} task
          where event.task_id = task.task_id
            and task.state in ('Completed', 'Failed', 'Canceled')
            and coalesce(task.completed_at_utc, task.failed_at_utc, task.canceled_at_utc) < transaction_timestamp() - @retention;
          """,
          command => command.Parameters.AddWithValue("retention", retention),
          cancellationToken)
          .ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyDictionary<TaskState, int>> ReadStateCountsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select state, count(*)
          from {context.Names.Tasks}
          group by state;
          """;

        var counts = Enum.GetValues<TaskState>().ToDictionary(state => state, _ => 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[PostgresConversion.ToTaskState(reader.GetValue(0))] = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static async ValueTask<IReadOnlyList<DashboardJobSummary>> ReadSummaryPageAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        string clause,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          {TaskSelectSql(context)}
          {clause};
          """;
        configure(command);

        var rows = await ReadRowsAsync(command, cancellationToken).ConfigureAwait(false);
        var jobs = new List<DashboardJobSummary>(rows.Count);
        foreach (var row in rows)
        {
            jobs.Add(await CreateSummaryAsync(context, connection, row, cancellationToken).ConfigureAwait(false));
        }

        return jobs;
    }

    private static async ValueTask<DashboardJobSummary> CreateSummaryAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        PostgresDashboardTaskRow row,
        CancellationToken cancellationToken)
      => new(
        row.TaskId,
        row.State,
        row.ServiceType,
        row.MethodName,
        row.Priority,
        row.EnqueueSequence,
        row.EnqueuedAtUtc,
        row.NotBeforeUtc,
        row.AttemptCount,
        row.MaxAttempts,
        await PostgresTaskTags.ReadTaskTagsAsync(context, connection, row.TaskId, cancellationToken).ConfigureAwait(false),
        row.SourceScheduleKey,
        await ReadLatestProgressAsync(context, connection, row.TaskId, cancellationToken).ConfigureAwait(false),
        await GetQueuePositionAsync(context, row.TaskId, cancellationToken).ConfigureAwait(false),
        row.CompletedAtUtc,
        row.FailedAtUtc,
        row.CanceledAtUtc);

    private static async ValueTask<DashboardProgressSnapshot?> ReadLatestProgressAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select progress_percent, message, occurred_at_utc
          from {context.Names.DashboardEvents}
          where task_id = @task_id
            and kind = 'Progress'
          order by event_sequence desc
          limit 1;
          """;
        command.Parameters.AddWithValue("task_id", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DashboardProgressSnapshot(
          reader.IsDBNull(0) ? null : reader.GetDouble(0),
          reader.IsDBNull(1) ? null : reader.GetString(1),
          PostgresConversion.ToDateTimeOffset(reader.GetValue(2)));
    }

    private static async ValueTask<long?> ReadClaimablePositionAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          with claimable as (
              select
                  task.task_id,
                  row_number() over (order by task.priority desc, task.enqueue_sequence asc) as position
              from {context.Names.Tasks} task
              where task.state = 'Queued'
                and (task.not_before_utc is null or task.not_before_utc <= transaction_timestamp())
                and not exists (
                    select 1
                    from {context.Names.TaskConcurrencyGroups} task_group
                    left join {context.Names.ConcurrencyGroups} concurrency_group on concurrency_group.group_key = task_group.group_key
                    where task_group.task_id = task.task_id
                      and coalesce(concurrency_group.in_use_count, 0) >= coalesce(concurrency_group.configured_limit, 1)
                )
          )
          select position
          from claimable
          where task_id = @task_id;
          """;
        command.Parameters.AddWithValue("task_id", taskId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async ValueTask<PostgresDashboardTaskRow?> ReadTaskRowAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          {TaskSelectSql(context)}
          where task.task_id = @task_id;
          """;
        command.Parameters.AddWithValue("task_id", taskId);

        var rows = await ReadRowsAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async ValueTask<IReadOnlyList<PostgresDashboardTaskRow>> ReadRowsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<PostgresDashboardTaskRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PostgresDashboardTaskRow(
              reader.GetGuid(0),
              PostgresConversion.ToTaskState(reader.GetValue(1)),
              reader.GetString(2),
              reader.GetString(3),
              reader.GetInt32(4),
              reader.GetInt64(5),
              PostgresConversion.ToDateTimeOffset(reader.GetValue(6)),
              reader.IsDBNull(7) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(7)),
              reader.GetInt32(8),
              reader.GetInt32(9),
              reader.IsDBNull(10) ? null : reader.GetString(10),
              reader.IsDBNull(11) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(11)),
              reader.IsDBNull(12) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(12)),
              reader.IsDBNull(13) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(13)),
              reader.IsDBNull(14) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(14)),
              reader.IsDBNull(15) ? null : reader.GetString(15),
              reader.IsDBNull(16) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(16)),
              reader.IsDBNull(17) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(17))));
        }

        return rows;
    }

    private static async ValueTask<IReadOnlyList<DashboardJobEvent>> ReadEventPageAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid taskId,
        long? afterEventSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var afterClause = afterEventSequence is null ? string.Empty : "and event_sequence > @after_event_sequence";
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
            {afterClause}
          order by event_sequence asc
          limit @limit;
          """;
        command.Parameters.AddWithValue("task_id", taskId);
        if (afterEventSequence is not null)
        {
            command.Parameters.AddWithValue("after_event_sequence", afterEventSequence.Value);
        }

        command.Parameters.AddWithValue("limit", limit);

        var events = new List<DashboardJobEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(PostgresDashboardEvents.ReadEvent(reader));
        }

        return events;
    }

    private static async ValueTask<DateTimeOffset> ReadCurrentTimestampAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select transaction_timestamp();";
        return PostgresConversion.ToDateTimeOffset(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
          ?? throw new InvalidOperationException("PostgreSQL did not return transaction_timestamp()."));
    }

    private static string TaskSelectSql(PostgresOperationContext context)
      => $"""
         select
             task.task_id,
             task.state,
             task.service_type,
             task.method_name,
             task.priority,
             task.enqueue_sequence,
             task.enqueued_at_utc,
             task.not_before_utc,
             task.attempt_count,
             task.max_attempts,
             task.source_schedule_key,
             task.completed_at_utc,
             task.failed_at_utc,
             task.canceled_at_utc,
             task.claimed_at_utc,
             task.claimed_by_node_id,
             task.lease_expires_at_utc,
             task.scheduled_fire_at_utc
         from {context.Names.Tasks} task
         """;

    private static void ValidateJobQuery(DashboardJobQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, "Dashboard job query page size must be positive.");
        }

        if (query.Tag is { } tag && (tag.Name.Trim().Length == 0 || tag.Value.Trim().Length == 0))
        {
            throw new ArgumentException("Dashboard tag filters must have non-empty name and value.", nameof(query));
        }

        _ = DecodeContinuationToken(query.ContinuationToken);
    }

    private static void ValidateEventQuery(DashboardEventQuery query)
    {
        if (query.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Dashboard event query limit must be positive.");
        }
    }

    private static long? DecodeContinuationToken(string? continuationToken)
    {
        if (continuationToken is null)
        {
            return null;
        }

        if (!long.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var enqueueSequence) || enqueueSequence <= 0)
        {
            throw new ArgumentException("Dashboard job continuation token is invalid.", nameof(continuationToken));
        }

        return enqueueSequence;
    }

    private sealed record PostgresDashboardTaskRow(
        Guid TaskId,
        TaskState State,
        string ServiceType,
        string MethodName,
        int Priority,
        long EnqueueSequence,
        DateTimeOffset EnqueuedAtUtc,
        DateTimeOffset? NotBeforeUtc,
        int AttemptCount,
        int MaxAttempts,
        string? SourceScheduleKey,
        DateTimeOffset? CompletedAtUtc,
        DateTimeOffset? FailedAtUtc,
        DateTimeOffset? CanceledAtUtc,
        DateTimeOffset? ClaimedAtUtc,
        string? ClaimedByNodeId,
        DateTimeOffset? LeaseExpiresAtUtc,
        DateTimeOffset? ScheduledFireAtUtc);
}
