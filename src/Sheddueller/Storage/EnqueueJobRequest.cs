namespace Sheddueller.Storage;

using Sheddueller.Serialization;

/// <summary>
/// Store request for enqueuing a job.
/// </summary>
public sealed record EnqueueJobRequest(
    Guid JobId,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedJobPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? NotBeforeUtc = null,
    int MaxAttempts = 1,
    RetryBackoffKind? RetryBackoffKind = null,
    TimeSpan? RetryBaseDelay = null,
    TimeSpan? RetryMaxDelay = null,
    string? SourceScheduleKey = null,
    DateTimeOffset? ScheduledFireAtUtc = null,
    IReadOnlyList<JobTag>? Tags = null);
