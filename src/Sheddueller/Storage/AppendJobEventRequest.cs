namespace Sheddueller.Storage;

using Sheddueller;

/// <summary>
/// Request to append a durable job event.
/// </summary>
public sealed record AppendJobEventRequest(
    Guid JobId,
    JobEventKind Kind,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);
