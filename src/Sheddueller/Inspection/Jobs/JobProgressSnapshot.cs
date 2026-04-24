namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Latest job progress snapshot.
/// </summary>
public sealed record JobProgressSnapshot(
    double? Percent,
    string? Message,
    DateTimeOffset ReportedAtUtc);
