namespace Sheddueller.Storage;

using Sheddueller.Serialization;

/// <summary>
/// A job claimed by a worker node.
/// </summary>
public sealed record ClaimedJob(
    Guid JobId,
    long EnqueueSequence,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedJobPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    int AttemptCount,
    int MaxAttempts,
    Guid LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc,
    RetryBackoffKind? RetryBackoffKind,
    TimeSpan? RetryBaseDelay,
    TimeSpan? RetryMaxDelay,
    string? SourceScheduleKey,
    DateTimeOffset? ScheduledFireAtUtc,
    JobInvocationTargetKind InvocationTargetKind = JobInvocationTargetKind.Instance,
    IReadOnlyList<JobMethodParameterBinding>? MethodParameterBindings = null);
