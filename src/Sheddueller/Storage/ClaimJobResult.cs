#pragma warning disable CA1034 // Nested types should not be visible

namespace Sheddueller.Storage;

/// <summary>
/// Store result for claim attempts.
/// </summary>
public abstract record ClaimJobResult
{
    /// <summary>
    /// A job was claimed.
    /// </summary>
    public sealed record Claimed(ClaimedJob Job) : ClaimJobResult;

    /// <summary>
    /// No job is currently claimable.
    /// </summary>
    public sealed record NoJobAvailable : ClaimJobResult;
}
