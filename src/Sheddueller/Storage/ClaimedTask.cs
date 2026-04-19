namespace Sheddueller.Storage;

using Sheddueller.Serialization;

/// <summary>
/// A task claimed by a worker node.
/// </summary>
public sealed record ClaimedTask(
    Guid TaskId,
    long EnqueueSequence,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedTaskPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    int AttemptCount,
    int MaxAttempts,
    Guid LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc,
    RetryBackoffKind? RetryBackoffKind,
    TimeSpan? RetryBaseDelay,
    TimeSpan? RetryMaxDelay,
    string? SourceScheduleKey,
    DateTimeOffset? ScheduledFireAtUtc);
