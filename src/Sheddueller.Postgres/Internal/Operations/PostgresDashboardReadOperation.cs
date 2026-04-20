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
        var queuedPage = await SearchJobsAsync(context, new DashboardJobQuery(State: JobState.Queued, PageSize: 100), cancellationToken).ConfigureAwait(false);

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
        void configureFilters(NpgsqlCommand command, List<string> conditions)
        {
            if (query.JobId is { } jobId)
            {
                conditions.Add("job.job_id = @job_id");
                command.Parameters.AddWithValue("job_id", jobId);
            }

            if (query.State is { } state)
            {
                conditions.Add("job.state = @state");
                command.Parameters.AddWithValue("state", PostgresConversion.ToText(state));
            }

            if (query.ServiceType is not null)
            {
                conditions.Add("job.service_type = @service_type");
                command.Parameters.AddWithValue("service_type", query.ServiceType);
            }

            if (query.MethodName is not null)
            {
                conditions.Add("job.method_name = @method_name");
                command.Parameters.AddWithValue("method_name", query.MethodName);
            }

            if (query.Tag is { } tag)
            {
                conditions.Add($"exists (select 1 from {context.Names.JobTags} tag where tag.job_id = job.job_id and tag.name = @tag_name and tag.value = @tag_value)");
                command.Parameters.AddWithValue("tag_name", tag.Name.Trim());
                command.Parameters.AddWithValue("tag_value", tag.Value.Trim());
            }

            if (query.SourceScheduleKey is not null)
            {
                conditions.Add("job.source_schedule_key = @source_schedule_key");
                command.Parameters.AddWithValue("source_schedule_key", query.SourceScheduleKey);
            }

            if (query.EnqueuedFromUtc is { } enqueuedFromUtc)
            {
                conditions.Add("job.enqueued_at_utc >= @enqueued_from_utc");
                command.Parameters.AddWithValue("enqueued_from_utc", enqueuedFromUtc);
            }

            if (query.EnqueuedToUtc is { } enqueuedToUtc)
            {
                conditions.Add("job.enqueued_at_utc <= @enqueued_to_utc");
                command.Parameters.AddWithValue("enqueued_to_utc", enqueuedToUtc);
            }

            if (query.TerminalFromUtc is { } terminalFromUtc)
            {
                conditions.Add("coalesce(job.completed_at_utc, job.failed_at_utc, job.canceled_at_utc) >= @terminal_from_utc");
                command.Parameters.AddWithValue("terminal_from_utc", terminalFromUtc);
            }

            if (query.TerminalToUtc is { } terminalToUtc)
            {
                conditions.Add("coalesce(job.completed_at_utc, job.failed_at_utc, job.canceled_at_utc) <= @terminal_to_utc");
                command.Parameters.AddWithValue("terminal_to_utc", terminalToUtc);
            }
        }

        await using var countCommand = connection.CreateCommand();
        var countConditions = new List<string>();
        configureFilters(countCommand, countConditions);
        countCommand.CommandText =
          $"""
          select count(*)
          from {context.Names.Jobs} job
          {CreateWhereClause(countConditions)};
          """;
        var totalCount = Convert.ToInt64(
          await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
          CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        configureFilters(command, conditions);
        if (afterSequence is { } sequence)
        {
            conditions.Add("job.enqueue_sequence < @after_sequence");
            command.Parameters.AddWithValue("after_sequence", sequence);
        }

        command.Parameters.AddWithValue("limit", query.PageSize + 1);
        var whereClause = CreateWhereClause(conditions);
        command.CommandText =
          $"""
          {JobSelectSql(context)}
          {whereClause}
          order by job.enqueue_sequence desc
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

        return new DashboardJobPage(jobs, continuationToken)
        {
            TotalCount = totalCount,
        };
    }

    public static async ValueTask<DashboardJobDetail?> GetJobAsync(
        PostgresOperationContext context,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await ReadJobRowAsync(context, connection, jobId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var events = await ReadEventPageAsync(context, connection, jobId, afterEventSequence: null, limit: 100, cancellationToken).ConfigureAwait(false);
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
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await ReadJobRowAsync(context, connection, jobId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.NotFound, Position: null, "Job was not found.");
        }

        if (row.State == JobState.Canceled)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Canceled, Position: null, "Job was canceled.");
        }

        if (row.State is JobState.Completed or JobState.Failed)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Terminal, Position: null, "Job is terminal.");
        }

        if (row.State == JobState.Claimed)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Claimed, Position: null, "Job is currently claimed.");
        }

        var now = await ReadCurrentTimestampAsync(connection, cancellationToken).ConfigureAwait(false);
        if (row.NotBeforeUtc is { } notBeforeUtc && notBeforeUtc > now)
        {
            return row.FailedAtUtc is null
              ? new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Delayed, Position: null, $"Job is delayed until {notBeforeUtc:O}.")
              : new DashboardQueuePosition(jobId, DashboardQueuePositionKind.RetryWaiting, Position: null, $"Job is waiting to retry until {notBeforeUtc:O}.");
        }

        var position = await ReadClaimablePositionAsync(context, connection, jobId, cancellationToken).ConfigureAwait(false);
        if (position is not null)
        {
            return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.Claimable, position, "Job is currently claimable.");
        }

        return new DashboardQueuePosition(jobId, DashboardQueuePositionKind.BlockedByConcurrency, Position: null, "Job is blocked by concurrency group limits.");
    }

    public static async IAsyncEnumerable<DashboardJobEvent> ReadEventsAsync(
        PostgresOperationContext context,
        Guid jobId,
        DashboardEventQuery? query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        query ??= new DashboardEventQuery();
        ValidateEventQuery(query);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var events = await ReadEventPageAsync(context, connection, jobId, query.AfterEventSequence, query.Limit, cancellationToken).ConfigureAwait(false);
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
          using {context.Names.Jobs} job
          where event.job_id = job.job_id
            and job.state in ('Completed', 'Failed', 'Canceled')
            and coalesce(job.completed_at_utc, job.failed_at_utc, job.canceled_at_utc) < transaction_timestamp() - @retention;
          """,
          command => command.Parameters.AddWithValue("retention", retention),
          cancellationToken)
          .ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyDictionary<JobState, int>> ReadStateCountsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select state, count(*)
          from {context.Names.Jobs}
          group by state;
          """;

        var counts = Enum.GetValues<JobState>().ToDictionary(state => state, _ => 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[PostgresConversion.ToJobState(reader.GetValue(0))] = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static string CreateWhereClause(List<string> conditions)
      => conditions.Count == 0 ? string.Empty : $"where {string.Join(" and ", conditions)}";

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
          {JobSelectSql(context)}
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
        PostgresDashboardJobRow row,
        CancellationToken cancellationToken)
      => new(
        row.JobId,
        row.State,
        row.ServiceType,
        row.MethodName,
        row.Priority,
        row.EnqueueSequence,
        row.EnqueuedAtUtc,
        row.NotBeforeUtc,
        row.AttemptCount,
        row.MaxAttempts,
        await PostgresJobTags.ReadJobTagsAsync(context, connection, row.JobId, cancellationToken).ConfigureAwait(false),
        row.SourceScheduleKey,
        await ReadLatestProgressAsync(context, connection, row.JobId, cancellationToken).ConfigureAwait(false),
        await GetQueuePositionAsync(context, row.JobId, cancellationToken).ConfigureAwait(false),
        row.CompletedAtUtc,
        row.FailedAtUtc,
        row.CanceledAtUtc);

    private static async ValueTask<DashboardProgressSnapshot?> ReadLatestProgressAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select progress_percent, message, occurred_at_utc
          from {context.Names.DashboardEvents}
          where job_id = @job_id
            and kind = 'Progress'
          order by event_sequence desc
          limit 1;
          """;
        command.Parameters.AddWithValue("job_id", jobId);

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
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          with claimable as (
              select
                  job.job_id,
                  row_number() over (order by job.priority desc, job.enqueue_sequence asc) as position
              from {context.Names.Jobs} job
              where job.state = 'Queued'
                and (job.not_before_utc is null or job.not_before_utc <= transaction_timestamp())
                and not exists (
                    select 1
                    from {context.Names.JobConcurrencyGroups} job_group
                    left join {context.Names.ConcurrencyGroups} concurrency_group on concurrency_group.group_key = job_group.group_key
                    where job_group.job_id = job.job_id
                      and coalesce(concurrency_group.in_use_count, 0) >= coalesce(concurrency_group.configured_limit, 1)
                )
          )
          select position
          from claimable
          where job_id = @job_id;
          """;
        command.Parameters.AddWithValue("job_id", jobId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async ValueTask<PostgresDashboardJobRow?> ReadJobRowAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          {JobSelectSql(context)}
          where job.job_id = @job_id;
          """;
        command.Parameters.AddWithValue("job_id", jobId);

        var rows = await ReadRowsAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async ValueTask<IReadOnlyList<PostgresDashboardJobRow>> ReadRowsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<PostgresDashboardJobRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PostgresDashboardJobRow(
              reader.GetGuid(0),
              PostgresConversion.ToJobState(reader.GetValue(1)),
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
        Guid jobId,
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
              job_id,
              event_sequence,
              kind,
              occurred_at_utc,
              attempt_number,
              log_level,
              message,
              progress_percent,
              fields
          from {context.Names.DashboardEvents}
          where job_id = @job_id
            {afterClause}
          order by event_sequence asc
          limit @limit;
          """;
        command.Parameters.AddWithValue("job_id", jobId);
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

    private static string JobSelectSql(PostgresOperationContext context)
      => $"""
         select
             job.job_id,
             job.state,
             job.service_type,
             job.method_name,
             job.priority,
             job.enqueue_sequence,
             job.enqueued_at_utc,
             job.not_before_utc,
             job.attempt_count,
             job.max_attempts,
             job.source_schedule_key,
             job.completed_at_utc,
             job.failed_at_utc,
             job.canceled_at_utc,
             job.claimed_at_utc,
             job.claimed_by_node_id,
             job.lease_expires_at_utc,
             job.scheduled_fire_at_utc
         from {context.Names.Jobs} job
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

    private sealed record PostgresDashboardJobRow(
        Guid JobId,
        JobState State,
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
