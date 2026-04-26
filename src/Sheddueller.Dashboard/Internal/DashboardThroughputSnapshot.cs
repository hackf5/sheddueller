namespace Sheddueller.Dashboard.Internal;

internal sealed record DashboardThroughputSnapshot(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    TimeSpan BucketSize,
    IReadOnlyList<DashboardThroughputBucket> Buckets);
