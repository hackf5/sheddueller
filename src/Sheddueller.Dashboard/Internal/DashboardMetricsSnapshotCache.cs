namespace Sheddueller.Dashboard.Internal;

using System.Globalization;

using Sheddueller.Inspection.Metrics;

internal sealed class DashboardMetricsSnapshotCache(
    IMetricsInspectionReader reader,
    TimeProvider timeProvider) : IDashboardMetricsReader
{
    internal static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<MetricsCacheKey, MetricsCacheEntry> _cache = [];
    private readonly Dictionary<MetricsCacheKey, Task<MetricsCacheEntry>> _inflight = [];

    public async ValueTask<MetricsInspectionSnapshot> GetMetricsAsync(
        MetricsInspectionQuery query,
        CancellationToken cancellationToken = default)
    {
        var stableQuery = CreateStableQuery(query);
        var key = MetricsCacheKey.From(stableQuery);
        var nowUtc = timeProvider.GetUtcNow();
        Task<MetricsCacheEntry> readTask;
        var ownsRead = false;

        lock (this._gate)
        {
            if (this._cache.TryGetValue(key, out var entry)
                && nowUtc - entry.CachedAtUtc < TimeToLive)
            {
                return entry.Snapshot;
            }

            if (!this._inflight.TryGetValue(key, out readTask!))
            {
                readTask = this.ReadAndCacheAsync(key, stableQuery, cancellationToken);
                this._inflight[key] = readTask;
                ownsRead = true;
            }
        }

        try
        {
            return (await readTask.WaitAsync(cancellationToken).ConfigureAwait(false)).Snapshot;
        }
        finally
        {
            if (ownsRead)
            {
                lock (this._gate)
                {
                    if (this._inflight.TryGetValue(key, out var current) && ReferenceEquals(current, readTask))
                    {
                        this._inflight.Remove(key);
                    }
                }
            }
        }
    }

    private async Task<MetricsCacheEntry> ReadAndCacheAsync(
        MetricsCacheKey key,
        MetricsInspectionQuery query,
        CancellationToken cancellationToken)
    {
        var snapshot = await reader.GetMetricsAsync(query, cancellationToken).ConfigureAwait(false);
        var entry = new MetricsCacheEntry(snapshot, timeProvider.GetUtcNow());

        lock (this._gate)
        {
            this._cache[key] = entry;
        }

        return entry;
    }

    private static MetricsInspectionQuery CreateStableQuery(MetricsInspectionQuery query)
      => query.Windows is { Count: > 0 } windows
        ? new MetricsInspectionQuery([.. windows])
        : new MetricsInspectionQuery();

    private readonly record struct MetricsCacheKey(string Value)
    {
        public static MetricsCacheKey From(MetricsInspectionQuery query)
          => query.Windows is { Count: > 0 } windows
            ? new MetricsCacheKey(string.Join("|", windows.Select(static window => window.Ticks.ToString(CultureInfo.InvariantCulture))))
            : new MetricsCacheKey("<default>");
    }

    private sealed record MetricsCacheEntry(
        MetricsInspectionSnapshot Snapshot,
        DateTimeOffset CachedAtUtc);
}
