namespace Sheddueller.Dashboard;

/// <summary>
/// Latest dashboard progress snapshot.
/// </summary>
public sealed record DashboardProgressSnapshot(
    double? Percent,
    string? Message,
    DateTimeOffset ReportedAtUtc);
