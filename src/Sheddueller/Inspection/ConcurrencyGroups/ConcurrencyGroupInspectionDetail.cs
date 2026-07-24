namespace Sheddueller.Inspection.ConcurrencyGroups;

/// <summary>
/// Concurrency group inspection detail.
/// </summary>
public sealed record ConcurrencyGroupInspectionDetail(
    ConcurrencyGroupInspectionSummary Summary,
    IReadOnlyList<Guid> ClaimedJobIds,
    IReadOnlyList<Guid> BlockedJobIds)
{
    /// <summary>
    /// Gets due queued jobs blocked by the concurrency limit.
    /// </summary>
    public IReadOnlyList<Guid> ConcurrencyBlockedJobIds { get; init; } = [];

    /// <summary>
    /// Gets due queued jobs blocked by the start-rate limit.
    /// </summary>
    public IReadOnlyList<Guid> RateBlockedJobIds { get; init; } = [];
}
