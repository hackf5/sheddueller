namespace Sheddueller;

/// <summary>
/// Result of creating or updating a recurring schedule.
/// </summary>
public enum RecurringScheduleUpsertResult
{
    /// <summary>
    /// A new schedule was created.
    /// </summary>
    Created,

    /// <summary>
    /// An existing schedule was changed.
    /// </summary>
    Updated,

    /// <summary>
    /// The submitted definition matched the existing schedule.
    /// </summary>
    Unchanged,
}
