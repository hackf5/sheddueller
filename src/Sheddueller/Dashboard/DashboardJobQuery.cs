namespace Sheddueller.Dashboard;

using Sheddueller.Storage;

/// <summary>
/// Dashboard job search query.
/// </summary>
public sealed record DashboardJobQuery(
    Guid? JobId = null,
    JobState? State = null,
    string? ServiceType = null,
    string? MethodName = null,
    JobTag? Tag = null,
    string? SourceScheduleKey = null,
    DateTimeOffset? EnqueuedFromUtc = null,
    DateTimeOffset? EnqueuedToUtc = null,
    DateTimeOffset? TerminalFromUtc = null,
    DateTimeOffset? TerminalToUtc = null,
    int PageSize = 100,
    string? ContinuationToken = null);
