namespace Sheddueller.Inspection.Schedules;

using Sheddueller.Storage;

/// <summary>
/// Inspection view of one materialized schedule occurrence.
/// </summary>
public sealed record ScheduleInspectionOccurrence(
    Guid JobId,
    DateTimeOffset? ScheduledFireAtUtc,
    ScheduleOccurrenceKind Kind,
    JobState State,
    DateTimeOffset MaterializedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? FailedAtUtc);
