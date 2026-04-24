namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;
using System.Runtime.CompilerServices;

using Npgsql;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Storage;

internal static class PostgresJobInspectionOperation
{
    public static async ValueTask<JobInspectionOverview> GetOverviewAsync(
        PostgresOperationContext context,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var counts = await ReadStateCountsAsync(context, connection, cancellationToken).ConfigureAwait(false);
        var running = await ReadSummaryPageAsync(context, connection, "where state = 'Claimed' order by claimed_at_utc desc nulls last, enqueue_sequence desc limit 10", static _ => { }, cancellationToken)
          .ConfigureAwait(false);
        var recentlyFailed = await ReadSummaryPageAsync(context, connection, "where state = 'Failed' order by failed_at_utc desc nulls last, enqueue_sequence desc limit 10", static _ => { }, cancellationToken)
          .ConfigureAwait(false);
        var queuedPage = await SearchJobsAsync(context, new JobInspectionQuery(States: [JobState.Queued], PageSize: 100), cancellationToken).ConfigureAwait(false);

        return new JobInspectionOverview(
          counts,
          running,
          recentlyFailed,
          [.. queuedPage.Jobs.Where(job => job.QueuePosition?.Kind == JobQueuePositionKind.Claimable).Take(10)],
          [.. queuedPage.Jobs.Where(job => job.QueuePosition?.Kind == JobQueuePositionKind.Delayed).Take(10)],
          [.. queuedPage.Jobs.Where(job => job.QueuePosition?.Kind == JobQueuePositionKind.RetryWaiting).Take(10)]);
    }

    public static async ValueTask<JobInspectionPage> SearchJobsAsync(
        PostgresOperationContext context,
        JobInspectionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateJobQuery(query);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var afterSequence = DecodeContinuationToken(query.ContinuationToken);
        void configureFilters(NpgsqlCommand command, List<string> conditions)
        {
            if (query.States is { Count: > 0 } states)
            {
                conditions.Add("job.state = any(@states)");
                command.Parameters.AddWithValue("states", states.Select(PostgresConversion.ToText).ToArray());
            }

            if (NormalizeContains(query.HandlerContains) is { } handlerContains)
            {
                conditions.Add("job.handler_search_text ilike @handler_contains escape '\\'");
                command.Parameters.AddWithValue("handler_contains", CreateContainsPattern(handlerContains));
            }

            if (NormalizeContains(query.TagContains) is { } tagContains)
            {
                conditions.Add($"exists (select 1 from {context.Names.JobTags} tag where tag.job_id = job.job_id and (tag.name || ':' || tag.value) ilike @tag_contains escape '\\')");
                command.Parameters.AddWithValue("tag_contains", CreateContainsPattern(tagContains));
            }

            if (NormalizeContains(query.ConcurrencyGroupContains) is { } groupContains)
            {
                conditions.Add($"exists (select 1 from {context.Names.JobConcurrencyGroups} job_group where job_group.job_id = job.job_id and job_group.group_key ilike @group_contains escape '\\')");
                command.Parameters.AddWithValue("group_contains", CreateContainsPattern(groupContains));
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
        var jobs = new List<JobInspectionSummary>(pageRows.Length);
        foreach (var row in pageRows)
        {
            jobs.Add(await CreateSummaryAsync(context, connection, row, cancellationToken).ConfigureAwait(false));
        }

        var continuationToken = rows.Count > query.PageSize
          ? pageRows[^1].EnqueueSequence.ToString(CultureInfo.InvariantCulture)
          : null;

        return new JobInspectionPage(jobs, continuationToken, totalCount);
    }

    public static async ValueTask<JobInspectionDetail?> GetJobAsync(
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

        return new JobInspectionDetail(
          await CreateSummaryAsync(context, connection, row, cancellationToken).ConfigureAwait(false),
          row.ClaimedAtUtc,
          row.ClaimedByNodeId,
          row.LeaseExpiresAtUtc,
          row.ScheduledFireAtUtc)
        {
            RetryCloneJobIds = await ReadRetryCloneJobIdsAsync(context, connection, jobId, cancellationToken).ConfigureAwait(false),
        };
    }

    public static async ValueTask<JobQueuePosition> GetQueuePositionAsync(
        PostgresOperationContext context,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await ReadJobRowAsync(context, connection, jobId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new JobQueuePosition(jobId, JobQueuePositionKind.NotFound, Position: null, "Job was not found.");
        }

        if (row.State == JobState.Canceled)
        {
            return new JobQueuePosition(jobId, JobQueuePositionKind.Canceled, Position: null, "Job was canceled.");
        }

        if (row.State is JobState.Completed or JobState.Failed)
        {
            return new JobQueuePosition(jobId, JobQueuePositionKind.Terminal, Position: null, "Job is terminal.");
        }

        if (row.State == JobState.Claimed)
        {
            return new JobQueuePosition(jobId, JobQueuePositionKind.Claimed, Position: null, "Job is currently claimed.");
        }

        var now = await ReadCurrentTimestampAsync(connection, cancellationToken).ConfigureAwait(false);
        if (row.NotBeforeUtc is { } notBeforeUtc && notBeforeUtc > now)
        {
            return row.FailedAtUtc is null
              ? new JobQueuePosition(jobId, JobQueuePositionKind.Delayed, Position: null, $"Job is delayed until {notBeforeUtc:O}.")
              : new JobQueuePosition(jobId, JobQueuePositionKind.RetryWaiting, Position: null, $"Job is waiting to retry until {notBeforeUtc:O}.");
        }

        var position = await ReadClaimablePositionAsync(context, connection, jobId, cancellationToken).ConfigureAwait(false);
        if (position is not null)
        {
            return new JobQueuePosition(jobId, JobQueuePositionKind.Claimable, position, "Job is currently claimable.");
        }

        return new JobQueuePosition(jobId, JobQueuePositionKind.BlockedByConcurrency, Position: null, "Job is blocked by concurrency group limits.");
    }

    public static async IAsyncEnumerable<JobEvent> ReadEventsAsync(
        PostgresOperationContext context,
        Guid jobId,
        JobEventReadOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        options ??= new JobEventReadOptions();
        ValidateEventReadOptions(options);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var events = await ReadEventPageAsync(context, connection, jobId, options.AfterEventSequence, options.Limit, cancellationToken).ConfigureAwait(false);
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
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "Job event retention must be positive.");
        }

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await PostgresOperationContext.ExecuteCountAsync(
          connection,
          $"""
          delete from {context.Names.JobEvents} event
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

    private static async ValueTask<IReadOnlyList<JobInspectionSummary>> ReadSummaryPageAsync(
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
        var jobs = new List<JobInspectionSummary>(rows.Count);
        foreach (var row in rows)
        {
            jobs.Add(await CreateSummaryAsync(context, connection, row, cancellationToken).ConfigureAwait(false));
        }

        return jobs;
    }

    private static async ValueTask<JobInspectionSummary> CreateSummaryAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        PostgresJobInspectionRow row,
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
        await PostgresJobGroups.ReadJobGroupKeysAsync(context, connection, transaction: null, row.JobId, cancellationToken).ConfigureAwait(false),
        row.SourceScheduleKey,
        await ReadLatestProgressAsync(context, connection, row.JobId, cancellationToken).ConfigureAwait(false),
        await GetQueuePositionAsync(context, row.JobId, cancellationToken).ConfigureAwait(false),
        row.ClaimedAtUtc,
        row.CompletedAtUtc,
        row.FailedAtUtc,
        row.CanceledAtUtc)
      {
          RetryCloneSourceJobId = row.RetryCloneSourceJobId,
          CancellationRequestedAtUtc = row.CancellationRequestedAtUtc,
          CancellationObservedAtUtc = row.CancellationObservedAtUtc,
          ScheduleOccurrenceKind = row.ScheduleOccurrenceKind,
      };

    private static async ValueTask<JobProgressSnapshot?> ReadLatestProgressAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select progress_percent, message, occurred_at_utc
          from {context.Names.JobEvents}
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

        return new JobProgressSnapshot(
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

    private static async ValueTask<PostgresJobInspectionRow?> ReadJobRowAsync(
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

    private static async ValueTask<IReadOnlyList<PostgresJobInspectionRow>> ReadRowsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<PostgresJobInspectionRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PostgresJobInspectionRow(
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
              reader.IsDBNull(17) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(17)),
              reader.IsDBNull(18) ? null : reader.GetGuid(18),
              reader.IsDBNull(19) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(19)),
              reader.IsDBNull(20) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(20)),
              PostgresConversion.ToScheduleOccurrenceKind(reader.GetValue(21))));
        }

        return rows;
    }

    private static async ValueTask<IReadOnlyList<JobEvent>> ReadEventPageAsync(
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
          from {context.Names.JobEvents}
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

        var events = new List<JobEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(PostgresJobEvents.ReadEvent(reader));
        }

        return events;
    }

    private static async ValueTask<IReadOnlyList<Guid>> ReadRetryCloneJobIdsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid sourceJobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select job_id
          from {context.Names.Jobs}
          where retry_clone_source_job_id = @source_job_id
          order by enqueue_sequence asc;
          """;
        command.Parameters.AddWithValue("source_job_id", sourceJobId);

        var jobIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobIds.Add(reader.GetGuid(0));
        }

        return jobIds;
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
             job.scheduled_fire_at_utc,
             job.retry_clone_source_job_id,
             job.cancellation_requested_at_utc,
             job.cancellation_observed_at_utc,
             job.schedule_occurrence_kind
         from {context.Names.Jobs} job
         """;

    private static void ValidateJobQuery(JobInspectionQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, "Job inspection query page size must be positive.");
        }

        if (query.States is not null)
        {
            foreach (var state in query.States)
            {
                if (!Enum.IsDefined(state))
                {
                    throw new ArgumentOutOfRangeException(nameof(query), state, "Job state is not supported.");
                }
            }
        }

        _ = DecodeContinuationToken(query.ContinuationToken);
    }

    private static string? NormalizeContains(string? value)
      => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateContainsPattern(string value)
      => string.Concat("%", EscapeLikePattern(value), "%");

    private static string EscapeLikePattern(string value)
      => value
        .Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal)
        .Replace("_", @"\_", StringComparison.Ordinal);

    private static void ValidateEventReadOptions(JobEventReadOptions options)
    {
        if (options.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Limit, "Job event read options limit must be positive.");
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
            throw new ArgumentException("Job inspection continuation token is invalid.", nameof(continuationToken));
        }

        return enqueueSequence;
    }

    private sealed record PostgresJobInspectionRow(
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
        DateTimeOffset? ScheduledFireAtUtc,
        Guid? RetryCloneSourceJobId,
        DateTimeOffset? CancellationRequestedAtUtc,
        DateTimeOffset? CancellationObservedAtUtc,
        ScheduleOccurrenceKind? ScheduleOccurrenceKind);
}
