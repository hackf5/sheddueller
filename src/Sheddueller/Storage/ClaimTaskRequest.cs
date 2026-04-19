#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Store request for claiming the next available task.
/// </summary>
public sealed record ClaimTaskRequest(
    string NodeId,
    DateTimeOffset ClaimedAtUtc);
