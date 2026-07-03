namespace Sheddueller.Dashboard.Internal;

using Sheddueller.Inspection.Metrics;

internal interface IDashboardMetricsReader
{
    ValueTask<MetricsInspectionSnapshot> GetMetricsAsync(
        MetricsInspectionQuery query,
        CancellationToken cancellationToken = default);
}
