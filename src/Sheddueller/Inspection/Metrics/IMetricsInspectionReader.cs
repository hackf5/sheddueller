namespace Sheddueller.Inspection.Metrics;

/// <summary>
/// Reads rolling scheduler metrics for inspection.
/// </summary>
public interface IMetricsInspectionReader
{
    /// <summary>
    /// Gets a rolling metrics snapshot.
    /// </summary>
    ValueTask<MetricsInspectionSnapshot> GetMetricsAsync(
        MetricsInspectionQuery query,
        CancellationToken cancellationToken = default);
}
