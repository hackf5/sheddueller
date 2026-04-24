namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;

using Npgsql;

using Sheddueller.Inspection.Nodes;

internal static class PostgresNodeInspectionOperation
{
    public static async ValueTask<NodeInspectionPage> SearchAsync(
        PostgresOperationContext context,
        NodeInspectionQuery query,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        ValidateQuery(query);
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var totalCount = await ReadTotalCountAsync(context, connection, query, staleThreshold, deadThreshold, cancellationToken).ConfigureAwait(false);
        var nodes = await ReadNodeSummaryPageAsync(context, connection, query, staleThreshold, deadThreshold, cancellationToken).ConfigureAwait(false);
        var pageItems = nodes.Take(query.PageSize).ToArray();

        return new NodeInspectionPage(
          pageItems,
          nodes.Count > query.PageSize ? pageItems[^1].NodeId : null,
          totalCount);
    }

    public static async ValueTask<NodeInspectionDetail?> GetAsync(
        PostgresOperationContext context,
        string nodeId,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var summary = await ReadNodeSummaryAsync(context, connection, nodeId, staleThreshold, deadThreshold, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            return null;
        }

        return new NodeInspectionDetail(
          summary,
          await ReadClaimedJobIdsAsync(context, connection, nodeId, cancellationToken).ConfigureAwait(false));
    }

    private static async ValueTask<long> ReadTotalCountAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NodeInspectionQuery query,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        ConfigureFilters(command, conditions, query);
        command.CommandText =
          $"""
          {NodeSummaryCteSql(context)}
          select count(*)
          from summary
          {CreateWhereClause(conditions)};
          """;
        command.Parameters.AddWithValue("stale_threshold", staleThreshold);
        command.Parameters.AddWithValue("dead_threshold", deadThreshold);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async ValueTask<IReadOnlyList<NodeInspectionSummary>> ReadNodeSummaryPageAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NodeInspectionQuery query,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        ConfigureFilters(command, conditions, query);
        if (query.ContinuationToken is not null)
        {
            conditions.Add("summary.node_id > @after_node_id");
            command.Parameters.AddWithValue("after_node_id", query.ContinuationToken);
        }

        command.CommandText =
          $"""
          {NodeSummaryCteSql(context)}
          select
              summary.node_id,
              summary.state,
              summary.first_seen_at_utc,
              summary.last_heartbeat_at_utc,
              summary.claimed_count,
              summary.max_concurrent_executions_per_node,
              summary.current_execution_count
          from summary
          {CreateWhereClause(conditions)}
          order by summary.node_id asc
          limit @limit;
          """;
        command.Parameters.AddWithValue("stale_threshold", staleThreshold);
        command.Parameters.AddWithValue("dead_threshold", deadThreshold);
        command.Parameters.AddWithValue("limit", query.PageSize + 1);

        return await ReadNodeSummariesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<NodeInspectionSummary?> ReadNodeSummaryAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        string nodeId,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          {NodeSummaryCteSql(context)}
          select
              summary.node_id,
              summary.state,
              summary.first_seen_at_utc,
              summary.last_heartbeat_at_utc,
              summary.claimed_count,
              summary.max_concurrent_executions_per_node,
              summary.current_execution_count
          from summary
          where summary.node_id = @node_id;
          """;
        command.Parameters.AddWithValue("node_id", nodeId);
        command.Parameters.AddWithValue("stale_threshold", staleThreshold);
        command.Parameters.AddWithValue("dead_threshold", deadThreshold);

        var rows = await ReadNodeSummariesAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async ValueTask<IReadOnlyList<NodeInspectionSummary>> ReadNodeSummariesAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var nodes = new List<NodeInspectionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            nodes.Add(new NodeInspectionSummary(
              reader.GetString(0),
              Enum.Parse<NodeHealthState>(reader.GetString(1)),
              PostgresConversion.ToDateTimeOffset(reader.GetValue(2)),
              PostgresConversion.ToDateTimeOffset(reader.GetValue(3)),
              Convert.ToInt32(reader.GetInt64(4), CultureInfo.InvariantCulture),
              reader.GetInt32(5),
              reader.GetInt32(6)));
        }

        return nodes;
    }

    private static async ValueTask<IReadOnlyList<Guid>> ReadClaimedJobIdsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        string nodeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select job_id
          from {context.Names.Jobs}
          where state = 'Claimed'
            and claimed_by_node_id = @node_id
          order by enqueue_sequence asc;
          """;
        command.Parameters.AddWithValue("node_id", nodeId);

        var jobIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobIds.Add(reader.GetGuid(0));
        }

        return jobIds;
    }

    private static void ConfigureFilters(
        NpgsqlCommand command,
        List<string> conditions,
        NodeInspectionQuery query)
    {
        if (query.State is { } state)
        {
            conditions.Add("summary.state = @state");
            command.Parameters.AddWithValue("state", state.ToString());
        }
    }

    private static string CreateWhereClause(List<string> conditions)
      => conditions.Count == 0 ? string.Empty : $"where {string.Join(" and ", conditions)}";

    private static string NodeSummaryCteSql(PostgresOperationContext context)
      => $"""
         with summary as (
             select
                 node.node_id,
                 case
                     when transaction_timestamp() - node.last_heartbeat_at_utc >= @dead_threshold then 'Dead'
                     when transaction_timestamp() - node.last_heartbeat_at_utc >= @stale_threshold then 'Stale'
                     else 'Active'
                 end as state,
                 node.first_seen_at_utc,
                 node.last_heartbeat_at_utc,
                 (
                     select count(*)
                     from {context.Names.Jobs} job
                     where job.state = 'Claimed'
                       and job.claimed_by_node_id = node.node_id
                 ) as claimed_count,
                 node.max_concurrent_executions_per_node,
                 node.current_execution_count
             from {context.Names.WorkerNodes} node
         )
         """;

    private static void ValidateQuery(NodeInspectionQuery query)
    {
        if (query.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, "Node inspection query page size must be positive.");
        }

        if (query.ContinuationToken is { Length: 0 })
        {
            throw new ArgumentException("Node inspection continuation token is invalid.", nameof(query));
        }
    }
}
