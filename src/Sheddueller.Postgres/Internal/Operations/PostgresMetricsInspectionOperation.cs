namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;

using Npgsql;

using Sheddueller.Inspection.Metrics;

internal static class PostgresMetricsInspectionOperation
{
    private static readonly TimeSpan[] DefaultMetricWindows = [TimeSpan.FromMinutes(5), TimeSpan.FromHours(1), TimeSpan.FromHours(24)];

    public static async ValueTask<MetricsInspectionSnapshot> GetAsync(
        PostgresOperationContext context,
        MetricsInspectionQuery query,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        var windows = query.Windows is { Count: > 0 } ? query.Windows : DefaultMetricWindows;
        if (windows.Any(window => window <= TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Windows, "Inspection metric windows must be positive.");
        }

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var metrics = new List<MetricsInspectionWindow>(windows.Count);
        foreach (var window in windows)
        {
            metrics.Add(await ReadWindowAsync(context, connection, window, staleThreshold, deadThreshold, cancellationToken).ConfigureAwait(false));
        }

        return new MetricsInspectionSnapshot(metrics);
    }

    private static async ValueTask<MetricsInspectionWindow> ReadWindowAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        TimeSpan window,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        var counts = await ReadCountsAsync(context, connection, window, staleThreshold, deadThreshold, cancellationToken).ConfigureAwait(false);
        var (queueLatencyP50, queueLatencyP95) = await ReadPercentilesAsync(
          connection,
          $"""
          select extract(epoch from (claimed_at_utc - enqueued_at_utc)) * 1000 as value_ms
          from {context.Names.Jobs}
          where claimed_at_utc >= transaction_timestamp() - @window
            and claimed_at_utc is not null
            and claimed_at_utc >= enqueued_at_utc
          """,
          window,
          cancellationToken)
          .ConfigureAwait(false);
        var (executionDurationP50, executionDurationP95) = await ReadPercentilesAsync(
          connection,
          $"""
          select extract(epoch from (coalesce(completed_at_utc, failed_at_utc, canceled_at_utc) - claimed_at_utc)) * 1000 as value_ms
          from {context.Names.Jobs}
          where state in ('Completed', 'Failed', 'Canceled')
            and claimed_at_utc is not null
            and coalesce(completed_at_utc, failed_at_utc, canceled_at_utc) >= transaction_timestamp() - @window
            and coalesce(completed_at_utc, failed_at_utc, canceled_at_utc) >= claimed_at_utc
          """,
          window,
          cancellationToken)
          .ConfigureAwait(false);
        var (_, scheduleFireLagP95) = await ReadPercentilesAsync(
          connection,
          $"""
          select extract(epoch from (enqueued_at_utc - scheduled_fire_at_utc)) * 1000 as value_ms
          from {context.Names.Jobs}
          where schedule_occurrence_kind = 'Automatic'
            and scheduled_fire_at_utc is not null
            and enqueued_at_utc >= transaction_timestamp() - @window
            and enqueued_at_utc >= scheduled_fire_at_utc
          """,
          window,
          cancellationToken)
          .ConfigureAwait(false);

        var minutes = Math.Max(window.TotalMinutes, double.Epsilon);
        return new MetricsInspectionWindow(
          window,
          counts.QueuedCount,
          counts.ClaimedCount,
          counts.FailedCount,
          counts.CanceledCount,
          counts.OldestQueuedAge,
          counts.EnqueuedCount / minutes,
          counts.ClaimedStartedCount / minutes,
          counts.SucceededCount / minutes,
          counts.FailedCount / minutes,
          counts.CanceledCount / minutes,
          counts.RetryEventCount / minutes,
          queueLatencyP50,
          queueLatencyP95,
          executionDurationP50,
          executionDurationP95,
          scheduleFireLagP95,
          counts.SaturatedGroupCount,
          counts.ActiveNodeCount,
          counts.StaleNodeCount,
          counts.DeadNodeCount);
    }

    private static async ValueTask<PostgresMetricsCounts> ReadCountsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        TimeSpan window,
        TimeSpan staleThreshold,
        TimeSpan deadThreshold,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          with current_counts as (
              select
                  count(*) filter (where state = 'Queued') as queued_count,
                  count(*) filter (where state = 'Claimed') as claimed_count,
                  max(transaction_timestamp() - enqueued_at_utc) filter (where state = 'Queued') as oldest_queued_age
              from {context.Names.Jobs}
          ),
          terminal_counts as (
              select
                  count(*) filter (where state = 'Completed' and completed_at_utc >= transaction_timestamp() - @window) as succeeded_count,
                  count(*) filter (where state = 'Failed' and failed_at_utc >= transaction_timestamp() - @window) as failed_count,
                  count(*) filter (where state = 'Canceled' and canceled_at_utc >= transaction_timestamp() - @window) as canceled_count
              from {context.Names.Jobs}
          ),
          event_counts as (
              select
                  count(*) filter (where kind = 'AttemptStarted' and occurred_at_utc >= transaction_timestamp() - @window) as claimed_started_count,
                  count(*) filter (where kind = 'AttemptFailed' and occurred_at_utc >= transaction_timestamp() - @window) as retry_event_count
              from {context.Names.JobEvents}
          ),
          enqueue_counts as (
              select count(*) as enqueued_count
              from {context.Names.Jobs}
              where enqueued_at_utc >= transaction_timestamp() - @window
          ),
          saturated_groups as (
              select count(*) as saturated_group_count
              from {context.Names.ConcurrencyGroups}
              where in_use_count >= coalesce(configured_limit, 1)
          ),
          node_counts as (
              select
                  count(*) filter (where transaction_timestamp() - last_heartbeat_at_utc < @stale_threshold) as active_node_count,
                  count(*) filter (where transaction_timestamp() - last_heartbeat_at_utc >= @stale_threshold and transaction_timestamp() - last_heartbeat_at_utc < @dead_threshold) as stale_node_count,
                  count(*) filter (where transaction_timestamp() - last_heartbeat_at_utc >= @dead_threshold) as dead_node_count
              from {context.Names.WorkerNodes}
          )
          select
              current_counts.queued_count,
              current_counts.claimed_count,
              current_counts.oldest_queued_age,
              terminal_counts.succeeded_count,
              terminal_counts.failed_count,
              terminal_counts.canceled_count,
              event_counts.claimed_started_count,
              event_counts.retry_event_count,
              enqueue_counts.enqueued_count,
              saturated_groups.saturated_group_count,
              node_counts.active_node_count,
              node_counts.stale_node_count,
              node_counts.dead_node_count
          from current_counts, terminal_counts, event_counts, enqueue_counts, saturated_groups, node_counts;
          """;
        command.Parameters.AddWithValue("window", window);
        command.Parameters.AddWithValue("stale_threshold", staleThreshold);
        command.Parameters.AddWithValue("dead_threshold", deadThreshold);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("PostgreSQL did not return inspection metrics.");
        }

        return new PostgresMetricsCounts(
          Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture),
          reader.IsDBNull(2) ? null : reader.GetTimeSpan(2),
          Convert.ToInt32(reader.GetInt64(3), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(4), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(5), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(6), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(7), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(8), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(9), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(10), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(11), CultureInfo.InvariantCulture),
          Convert.ToInt32(reader.GetInt64(12), CultureInfo.InvariantCulture));
    }

    private static async ValueTask<(TimeSpan? P50, TimeSpan? P95)> ReadPercentilesAsync(
        NpgsqlConnection connection,
        string sourceSql,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          with values_ms as (
              {sourceSql}
          )
          select
              percentile_cont(0.5) within group (order by value_ms) as p50,
              percentile_cont(0.95) within group (order by value_ms) as p95
          from values_ms;
          """;
        command.Parameters.AddWithValue("window", window);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (null, null);
        }

        return (
          reader.IsDBNull(0) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(0)),
          reader.IsDBNull(1) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(1)));
    }

    private sealed record PostgresMetricsCounts(
        int QueuedCount,
        int ClaimedCount,
        TimeSpan? OldestQueuedAge,
        int SucceededCount,
        int FailedCount,
        int CanceledCount,
        int ClaimedStartedCount,
        int RetryEventCount,
        int EnqueuedCount,
        int SaturatedGroupCount,
        int ActiveNodeCount,
        int StaleNodeCount,
        int DeadNodeCount);
}
