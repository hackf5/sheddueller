namespace Sheddueller.Dashboard.Internal;

using Sheddueller.Storage;

internal sealed class DashboardThroughputStore : IDashboardThroughputReader, IDisposable
{
    private const int BucketSizeSeconds = 5;

    internal static readonly TimeSpan BucketSize = TimeSpan.FromSeconds(BucketSizeSeconds);
    internal static readonly TimeSpan Window = TimeSpan.FromHours(1);
    internal const int BucketCount = 720;

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
        var windowEndUtc = TruncateToBucket(this._timeProvider.GetUtcNow());
        var windowStartUtc = windowEndUtc.AddSeconds(-(BucketCount - 1) * BucketSizeSeconds);
        var buckets = new DashboardThroughputBucket[BucketCount];

        lock (this._gate)
        {
            for (var offset = 0; offset < BucketCount; offset++)
            {
                var bucketStartUtc = windowStartUtc.AddSeconds(offset * BucketSizeSeconds);
                var bucketNumber = BucketNumber(bucketStartUtc);
                var state = this._buckets[BucketIndex(bucketNumber)];
                buckets[offset] = state.BucketNumber == bucketNumber
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

        var occurredAtUtc = jobEvent.OccurredAtUtc.ToUniversalTime();
        var nowUtc = this._timeProvider.GetUtcNow();
        var nowBucketUtc = TruncateToBucket(nowUtc);
        var eventBucketUtc = TruncateToBucket(occurredAtUtc);
        if (eventBucketUtc <= nowBucketUtc.Subtract(Window) || occurredAtUtc > nowUtc)
        {
            return;
        }

        var bucketNumber = BucketNumber(eventBucketUtc);
        var index = BucketIndex(bucketNumber);
        lock (this._gate)
        {
            ref var state = ref this._buckets[index];
            if (state.BucketNumber != bucketNumber)
            {
                state = new DashboardThroughputBucketState(bucketNumber);
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

    private static DateTimeOffset TruncateToBucket(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var unixSeconds = utc.ToUnixTimeSeconds();
        var bucketStartUnixSeconds = unixSeconds - (unixSeconds % BucketSizeSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(bucketStartUnixSeconds);
    }

    private static long BucketNumber(DateTimeOffset timestamp)
      => timestamp.ToUnixTimeSeconds() / BucketSizeSeconds;

    private static int BucketIndex(long bucketNumber)
      => (int)(((bucketNumber % BucketCount) + BucketCount) % BucketCount);

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

    private struct DashboardThroughputBucketState(long bucketNumber)
    {
        public long BucketNumber { get; private set; } = bucketNumber;

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
