namespace Sheddueller.Dashboard;

/// <summary>
/// Durable dashboard job event.
/// </summary>
public sealed record DashboardJobEvent(
    Guid EventId,
    Guid JobId,
    long EventSequence,
    DashboardJobEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);
