namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;

using Npgsql;

using Sheddueller.Inspection.ConcurrencyGroups;

internal static class PostgresConcurrencyGroupInspectionOperation
{
    public static async ValueTask<ConcurrencyGroupInspectionPage> SearchAsync(
        PostgresOperationContext context,
        ConcurrencyGroupInspectionQuery query,
        CancellationToken cancellationToken)
    {
        ValidateQuery(query);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var totalCount = await ReadTotalCountAsync(context, connection, query, cancellationToken).ConfigureAwait(false);
        var groups = await ReadGroupSummaryPageAsync(context, connection, query, cancellationToken).ConfigureAwait(false);
        var pageItems = groups.Take(query.PageSize).ToArray();

        return new ConcurrencyGroupInspectionPage(
          pageItems,
          groups.Count > query.PageSize ? pageItems[^1].GroupKey : null,
          totalCount);
    }

    public static async ValueTask<ConcurrencyGroupInspectionDetail?> GetAsync(
        PostgresOperationContext context,
        string groupKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var summary = await ReadGroupSummaryAsync(context, connection, groupKey, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            return null;
        }

        return new ConcurrencyGroupInspectionDetail(
          summary,
          await ReadClaimedJobIdsAsync(context, connection, groupKey, cancellationToken).ConfigureAwait(false),
          await ReadBlockedJobIdsAsync(context, connection, groupKey, cancellationToken).ConfigureAwait(false));
    }

    private static async ValueTask<long> ReadTotalCountAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        ConcurrencyGroupInspectionQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        ConfigureFilters(command, conditions, query);
        command.CommandText =
          $"""
          {GroupSummaryCteSql(context)}
          select count(*)
          from summary
          {CreateWhereClause(conditions)};
          """;

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async ValueTask<IReadOnlyList<ConcurrencyGroupInspectionSummary>> ReadGroupSummaryPageAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        ConcurrencyGroupInspectionQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        ConfigureFilters(command, conditions, query);
        if (query.ContinuationToken is not null)
        {
            conditions.Add("summary.group_key > @after_group_key");
            command.Parameters.AddWithValue("after_group_key", query.ContinuationToken);
        }

        command.Parameters.AddWithValue("limit", query.PageSize + 1);
        command.CommandText =
          $"""
          {GroupSummaryCteSql(context)}
          select
              summary.group_key,
              summary.effective_limit,
              summary.current_occupancy,
              summary.blocked_count,
              summary.is_saturated,
              summary.updated_at_utc
          from summary
          {CreateWhereClause(conditions)}
          order by summary.group_key asc
          limit @limit;
          """;

        return await ReadGroupSummariesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<ConcurrencyGroupInspectionSummary?> ReadGroupSummaryAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        string groupKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          {GroupSummaryCteSql(context)}
          select
              summary.group_key,
              summary.effective_limit,
              summary.current_occupancy,
              summary.blocked_count,
              summary.is_saturated,
              summary.updated_at_utc
          from summary
          where summary.group_key = @group_key;
          """;
        command.Parameters.AddWithValue("group_key", groupKey);

        var rows = await ReadGroupSummariesAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async ValueTask<IReadOnlyList<ConcurrencyGroupInspectionSummary>> ReadGroupSummariesAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var groups = new List<ConcurrencyGroupInspectionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var limit = reader.GetInt32(1);
            var occupancy = reader.GetInt32(2);
            groups.Add(new ConcurrencyGroupInspectionSummary(
              reader.GetString(0),
              limit,
              occupancy,
              Convert.ToInt32(reader.GetInt64(3), CultureInfo.InvariantCulture),
              reader.GetBoolean(4),
              reader.IsDBNull(5) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(5))));
        }

        return groups;
    }

    private static void ConfigureFilters(
        NpgsqlCommand command,
        List<string> conditions,
        ConcurrencyGroupInspectionQuery query)
    {
        if (query.GroupKey is not null)
        {
            conditions.Add("summary.group_key = @group_key");
            command.Parameters.AddWithValue("group_key", query.GroupKey);
        }

        if (query.IsSaturated is { } isSaturated)
        {
            conditions.Add("summary.is_saturated = @is_saturated");
            command.Parameters.AddWithValue("is_saturated", isSaturated);
        }

        if (query.HasBlockedJobs is { } hasBlockedJobs)
        {
            conditions.Add("(summary.blocked_count > 0) = @has_blocked_jobs");
            command.Parameters.AddWithValue("has_blocked_jobs", hasBlockedJobs);
        }
    }

    private static string CreateWhereClause(List<string> conditions)
      => conditions.Count == 0 ? string.Empty : $"where {string.Join(" and ", conditions)}";

    private static string GroupSummaryCteSql(PostgresOperationContext context)
      => $"""
         with group_keys as (
             select group_key from {context.Names.ConcurrencyGroups}
             union
             select group_key from {context.Names.JobConcurrencyGroups}
         ),
         blocked as (
             select job_group.group_key, count(*) as blocked_count
             from {context.Names.JobConcurrencyGroups} job_group
             join {context.Names.Jobs} job on job.job_id = job_group.job_id
             left join {context.Names.ConcurrencyGroups} concurrency_group on concurrency_group.group_key = job_group.group_key
             where job.state = 'Queued'
               and (job.not_before_utc is null or job.not_before_utc <= transaction_timestamp())
               and coalesce(concurrency_group.in_use_count, 0) >= coalesce(concurrency_group.configured_limit, 1)
             group by job_group.group_key
         ),
         summary as (
             select
                 group_keys.group_key,
                 coalesce(concurrency_group.configured_limit, 1) as effective_limit,
                 coalesce(concurrency_group.in_use_count, 0) as current_occupancy,
                 coalesce(blocked.blocked_count, 0) as blocked_count,
                 coalesce(concurrency_group.in_use_count, 0) >= coalesce(concurrency_group.configured_limit, 1) as is_saturated,
                 concurrency_group.updated_at_utc
             from group_keys
             left join {context.Names.ConcurrencyGroups} concurrency_group on concurrency_group.group_key = group_keys.group_key
             left join blocked on blocked.group_key = group_keys.group_key
         )
         """;

    private static async ValueTask<IReadOnlyList<Guid>> ReadClaimedJobIdsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        string groupKey,
        CancellationToken cancellationToken)
      => await ReadJobIdsAsync(
        connection,
        $"""
        select job.job_id
        from {context.Names.Jobs} job
        join {context.Names.JobConcurrencyGroups} job_group on job_group.job_id = job.job_id
        where job_group.group_key = @group_key
          and job.state = 'Claimed'
        order by job.enqueue_sequence asc;
        """,
        groupKey,
        cancellationToken)
        .ConfigureAwait(false);

    private static async ValueTask<IReadOnlyList<Guid>> ReadBlockedJobIdsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        string groupKey,
        CancellationToken cancellationToken)
      => await ReadJobIdsAsync(
        connection,
        $"""
        select job.job_id
        from {context.Names.Jobs} job
        join {context.Names.JobConcurrencyGroups} job_group on job_group.job_id = job.job_id
        left join {context.Names.ConcurrencyGroups} concurrency_group on concurrency_group.group_key = job_group.group_key
        where job_group.group_key = @group_key
          and job.state = 'Queued'
          and (job.not_before_utc is null or job.not_before_utc <= transaction_timestamp())
          and coalesce(concurrency_group.in_use_count, 0) >= coalesce(concurrency_group.configured_limit, 1)
        order by job.priority desc, job.enqueue_sequence asc;
        """,
        groupKey,
        cancellationToken)
        .ConfigureAwait(false);

    private static async ValueTask<IReadOnlyList<Guid>> ReadJobIdsAsync(
        NpgsqlConnection connection,
        string commandText,
        string groupKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("group_key", groupKey);

        var jobIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobIds.Add(reader.GetGuid(0));
        }

        return jobIds;
    }

    private static void ValidateQuery(ConcurrencyGroupInspectionQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, "Concurrency group inspection query page size must be positive.");
        }

        if (query.ContinuationToken is { Length: 0 })
        {
            throw new ArgumentException("Concurrency group inspection continuation token is invalid.", nameof(query));
        }
    }
}
