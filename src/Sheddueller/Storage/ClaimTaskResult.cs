#pragma warning disable CA1034 // Nested types should not be visible
#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace Sheddueller;

/// <summary>
/// Store result for claim attempts.
/// </summary>
public abstract record ClaimTaskResult
{
    /// <summary>
    /// A task was claimed.
    /// </summary>
    public sealed record Claimed(ClaimedTask Task) : ClaimTaskResult;

    /// <summary>
    /// No task is currently claimable.
    /// </summary>
    public sealed record NoTaskAvailable : ClaimTaskResult;
}
