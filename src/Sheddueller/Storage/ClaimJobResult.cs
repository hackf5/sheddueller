#pragma warning disable CA1034 // Nested types should not be visible

namespace Sheddueller.Storage;

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
