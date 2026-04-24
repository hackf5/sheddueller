namespace Sheddueller.Inspection.Metrics;

/// <summary>
/// Metrics inspection rolling metrics snapshot.
/// </summary>
public sealed record MetricsInspectionSnapshot(
    IReadOnlyList<MetricsInspectionWindow> Windows);
