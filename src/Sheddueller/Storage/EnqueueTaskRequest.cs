namespace Sheddueller.Storage;

using Sheddueller.Serialization;

/// <summary>
/// Store request for enqueuing a task.
/// </summary>
public sealed record EnqueueTaskRequest(
    Guid TaskId,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedTaskPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? NotBeforeUtc = null,
    int MaxAttempts = 1,
    RetryBackoffKind? RetryBackoffKind = null,
    TimeSpan? RetryBaseDelay = null,
    TimeSpan? RetryMaxDelay = null,
    string? SourceScheduleKey = null,
    DateTimeOffset? ScheduledFireAtUtc = null);
