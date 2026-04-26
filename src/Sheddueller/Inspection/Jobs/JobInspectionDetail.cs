namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Job inspection detail.
/// </summary>
public sealed record JobInspectionDetail(
    JobInspectionSummary Summary,
    DateTimeOffset? ClaimedAtUtc,
    string? ClaimedByNodeId,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? ScheduledFireAtUtc)
{
    /// <summary>
    /// Reconstructed persisted invocation metadata for this job.
    /// </summary>
    public JobInvocationInspection? Invocation { get; init; }

    /// <summary>
    /// Jobs cloned from this failed job.
    /// </summary>
    public IReadOnlyList<Guid> RetryCloneJobIds { get; init; } = [];
}
