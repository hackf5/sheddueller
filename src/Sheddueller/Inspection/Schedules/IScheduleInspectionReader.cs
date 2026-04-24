namespace Sheddueller.Inspection.Schedules;

/// <summary>
/// Reads recurring schedule inspection metadata.
/// </summary>
public interface IScheduleInspectionReader
{
    /// <summary>
    /// Searches recurring schedules using inspection filters.
    /// </summary>
    ValueTask<ScheduleInspectionPage> SearchSchedulesAsync(
        ScheduleInspectionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one recurring schedule detail record.
    /// </summary>
    ValueTask<ScheduleInspectionDetail?> GetScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default);
}
