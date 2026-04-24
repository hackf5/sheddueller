namespace Sheddueller.Storage;

using Sheddueller;

/// <summary>
/// Durable job event.
/// </summary>
public sealed record JobEvent(
    Guid EventId,
    Guid JobId,
    long EventSequence,
    JobEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    int AttemptNumber,
    JobLogLevel? LogLevel = null,
    string? Message = null,
    double? ProgressPercent = null,
    IReadOnlyDictionary<string, string>? Fields = null);
