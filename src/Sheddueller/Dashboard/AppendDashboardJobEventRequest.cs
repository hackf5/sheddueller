namespace Sheddueller.Dashboard;

/// <summary>
/// Request to append a dashboard job event.
/// </summary>
public sealed record AppendDashboardJobEventRequest(
    Guid JobId,
    DashboardJobEventKind Kind,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);
