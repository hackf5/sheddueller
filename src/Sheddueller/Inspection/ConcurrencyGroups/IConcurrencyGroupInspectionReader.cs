namespace Sheddueller.Inspection.ConcurrencyGroups;

/// <summary>
/// Reads concurrency group inspection metadata.
/// </summary>
public interface IConcurrencyGroupInspectionReader
{
    /// <summary>
    /// Searches concurrency groups using inspection filters.
    /// </summary>
    ValueTask<ConcurrencyGroupInspectionPage> SearchConcurrencyGroupsAsync(
        ConcurrencyGroupInspectionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one concurrency group detail record.
    /// </summary>
    ValueTask<ConcurrencyGroupInspectionDetail?> GetConcurrencyGroupAsync(
        string groupKey,
        CancellationToken cancellationToken = default);
}
