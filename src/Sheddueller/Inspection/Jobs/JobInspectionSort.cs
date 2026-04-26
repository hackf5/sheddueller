namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Job inspection result ordering.
/// </summary>
public enum JobInspectionSort
{
    /// <summary>
    /// Orders active work first, with queued jobs in claim order.
    /// </summary>
    Operational,

    /// <summary>
    /// Orders newest enqueued jobs first.
    /// </summary>
    NewestFirst,
}
