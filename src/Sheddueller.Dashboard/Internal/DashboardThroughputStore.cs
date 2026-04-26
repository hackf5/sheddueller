namespace Sheddueller.Dashboard.Internal;

using Sheddueller.Storage;

internal sealed class DashboardThroughputStore : IDashboardThroughputReader, IDisposable
{
    internal static readonly TimeSpan BucketSize = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan Window = TimeSpan.FromHours(1);
    internal const int BucketCount = 3600;

    private readonly Lock _gate = new();
    private readonly DashboardThroughputBucketState[] _buckets = new DashboardThroughputBucketState[BucketCount];
    private readonly DashboardLiveUpdateStream _stream;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public DashboardThroughputStore(
        DashboardLiveUpdateStream stream,
        TimeProvider timeProvider)
    {
        this._stream = stream;
        this._timeProvider = timeProvider;
        this._stream.JobEventPublished += this.RecordAsync;
    }

    public DashboardThroughputSnapshot GetSnapshot()
    {
        var windowEndUtc = TruncateToSecond(this._timeProvider.GetUtcNow());
        var windowStartUtc = windowEndUtc.AddSeconds(-(BucketCount - 1));
        var buckets = new DashboardThroughputBucket[BucketCount];

        lock (this._gate)
        {
            for (var offset = 0; offset < BucketCount; offset++)
            {
                var bucketStartUtc = windowStartUtc.AddSeconds(offset);
                var unixSecond = bucketStartUtc.ToUnixTimeSeconds();
                var state = this._buckets[BucketIndex(unixSecond)];
                buckets[offset] = state.UnixSecond == unixSecond
                  ? state.ToBucket(bucketStartUtc)
                  : EmptyBucket(bucketStartUtc);
            }
        }

        return new DashboardThroughputSnapshot(windowStartUtc, windowEndUtc, BucketSize, buckets);
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this._stream.JobEventPublished -= this.RecordAsync;
    }

    internal void Record(JobEvent jobEvent)
    {
        if (!TryClassify(jobEvent, out var metric))
        {
            return;
        }

        var eventBucketUtc = TruncateToSecond(jobEvent.OccurredAtUtc);
        var nowUtc = TruncateToSecond(this._timeProvider.GetUtcNow());
        if (eventBucketUtc <= nowUtc.Subtract(Window) || eventBucketUtc > nowUtc)
        {
            return;
        }

        var unixSecond = eventBucketUtc.ToUnixTimeSeconds();
        var index = BucketIndex(unixSecond);
        lock (this._gate)
        {
            ref var state = ref this._buckets[index];
            if (state.UnixSecond != unixSecond)
            {
                state = new DashboardThroughputBucketState(unixSecond);
            }

            state.Increment(metric);
        }
    }

    private ValueTask RecordAsync(
        JobEvent jobEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Record(jobEvent);
        return ValueTask.CompletedTask;
    }

    private static bool TryClassify(
        JobEvent jobEvent,
        out DashboardThroughputMetric metric)
    {
        metric = default;
        switch (jobEvent.Kind)
        {
            case JobEventKind.AttemptStarted:
                metric = DashboardThroughputMetric.Started;
                return true;
            case JobEventKind.AttemptCompleted:
                metric = DashboardThroughputMetric.Succeeded;
                return true;
            case JobEventKind.AttemptFailed:
                metric = DashboardThroughputMetric.FailedAttempt;
                return true;
            case JobEventKind.Lifecycle:
                return TryClassifyLifecycle(jobEvent.Message, out metric);
            default:
                return false;
        }
    }

    private static bool TryClassifyLifecycle(
        string? message,
        out DashboardThroughputMetric metric)
    {
        metric = default;
        if (string.Equals(message, "Queued", StringComparison.Ordinal))
        {
            metric = DashboardThroughputMetric.Queued;
            return true;
        }

        if (string.Equals(message, "Failed", StringComparison.Ordinal)
            || message?.StartsWith("Failed;", StringComparison.Ordinal) == true)
        {
            metric = DashboardThroughputMetric.Failed;
            return true;
        }

        if (string.Equals(message, "Canceled", StringComparison.Ordinal))
        {
            metric = DashboardThroughputMetric.Canceled;
            return true;
        }

        return false;
    }

    private static DateTimeOffset TruncateToSecond(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, TimeSpan.Zero);
    }

    private static int BucketIndex(long unixSecond)
      => (int)(((unixSecond % BucketCount) + BucketCount) % BucketCount);

    private static DashboardThroughputBucket EmptyBucket(DateTimeOffset startedAtUtc)
      => new(startedAtUtc, 0, 0, 0, 0, 0, 0);

    private enum DashboardThroughputMetric
    {
        Queued,
        Started,
        Succeeded,
        Failed,
        Canceled,
        FailedAttempt,
    }

    private struct DashboardThroughputBucketState(long unixSecond)
    {
        public long UnixSecond { get; private set; } = unixSecond;

        public int QueuedCount { get; private set; }

        public int StartedCount { get; private set; }

        public int SucceededCount { get; private set; }

        public int FailedCount { get; private set; }

        public int CanceledCount { get; private set; }

        public int FailedAttemptCount { get; private set; }

        public void Increment(DashboardThroughputMetric metric)
        {
            switch (metric)
            {
                case DashboardThroughputMetric.Queued:
                    this.QueuedCount++;
                    break;
                case DashboardThroughputMetric.Started:
                    this.StartedCount++;
                    break;
                case DashboardThroughputMetric.Succeeded:
                    this.SucceededCount++;
                    break;
                case DashboardThroughputMetric.Failed:
                    this.FailedCount++;
                    break;
                case DashboardThroughputMetric.Canceled:
                    this.CanceledCount++;
                    break;
                case DashboardThroughputMetric.FailedAttempt:
                    this.FailedAttemptCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(metric), metric, "Throughput metric is not supported.");
            }
        }

        public readonly DashboardThroughputBucket ToBucket(DateTimeOffset startedAtUtc)
          => new(
            startedAtUtc,
            this.QueuedCount,
            this.StartedCount,
            this.SucceededCount,
            this.FailedCount,
            this.CanceledCount,
            this.FailedAttemptCount);
    }
}
