namespace Sheddueller.Inspection.Metrics;

/// <summary>
/// Metrics inspection query.
/// </summary>
public sealed record MetricsInspectionQuery(
    IReadOnlyList<TimeSpan>? Windows = null);
