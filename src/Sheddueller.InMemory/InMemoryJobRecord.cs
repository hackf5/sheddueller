namespace Sheddueller;

using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class InMemoryJobRecord(
  Guid jobId,
  JobState state,
  int priority,
  long enqueueSequence,
  DateTimeOffset enqueuedAtUtc,
  string serviceType,
  string methodName,
  IReadOnlyList<string> methodParameterTypes,
  SerializedJobPayload serializedArguments,
  IReadOnlyList<string> concurrencyGroupKeys,
  DateTimeOffset? notBeforeUtc,
  int maxAttempts,
  RetryBackoffKind? retryBackoffKind,
  TimeSpan? retryBaseDelay,
  TimeSpan? retryMaxDelay,
  string? sourceScheduleKey,
  DateTimeOffset? scheduledFireAtUtc,
  IReadOnlyList<JobTag> tags)
{
    public Guid JobId { get; } = jobId;

    public JobState State { get; set; } = state;

    public int Priority { get; } = priority;

    public long EnqueueSequence { get; } = enqueueSequence;

    public DateTimeOffset EnqueuedAtUtc { get; } = enqueuedAtUtc;

    public string ServiceType { get; } = serviceType;

    public string MethodName { get; } = methodName;

    public IReadOnlyList<string> MethodParameterTypes { get; } = methodParameterTypes;

    public SerializedJobPayload SerializedArguments { get; } = serializedArguments;

    public IReadOnlyList<string> ConcurrencyGroupKeys { get; } = concurrencyGroupKeys;

    public DateTimeOffset? NotBeforeUtc { get; set; } = notBeforeUtc;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; } = maxAttempts;

    public RetryBackoffKind? RetryBackoffKind { get; } = retryBackoffKind;

    public TimeSpan? RetryBaseDelay { get; } = retryBaseDelay;

    public TimeSpan? RetryMaxDelay { get; } = retryMaxDelay;

    public Guid? LeaseToken { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public string? ClaimedByNodeId { get; set; }

    public DateTimeOffset? ClaimedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? FailedAtUtc { get; set; }

    public JobFailureInfo? Failure { get; set; }

    public DateTimeOffset? CanceledAtUtc { get; set; }

    public string? SourceScheduleKey { get; } = sourceScheduleKey;

    public DateTimeOffset? ScheduledFireAtUtc { get; } = scheduledFireAtUtc;

    public IReadOnlyList<JobTag> Tags { get; } = tags;

    public long DashboardEventSequence { get; set; }
}

internal sealed record InMemoryJobSnapshot(
  Guid JobId,
  JobState State,
  int Priority,
  long EnqueueSequence,
  DateTimeOffset EnqueuedAtUtc,
  string ServiceType,
  string MethodName,
  IReadOnlyList<string> MethodParameterTypes,
  SerializedJobPayload SerializedArguments,
  IReadOnlyList<string> ConcurrencyGroupKeys,
  DateTimeOffset? NotBeforeUtc,
  int AttemptCount,
  int MaxAttempts,
  RetryBackoffKind? RetryBackoffKind,
  TimeSpan? RetryBaseDelay,
  TimeSpan? RetryMaxDelay,
  Guid? LeaseToken,
  DateTimeOffset? LeaseExpiresAtUtc,
  DateTimeOffset? LastHeartbeatAtUtc,
  string? ClaimedByNodeId,
  DateTimeOffset? ClaimedAtUtc,
  DateTimeOffset? CompletedAtUtc,
  DateTimeOffset? FailedAtUtc,
  JobFailureInfo? Failure,
  DateTimeOffset? CanceledAtUtc,
  string? SourceScheduleKey,
  DateTimeOffset? ScheduledFireAtUtc,
  IReadOnlyList<JobTag> Tags);

internal sealed class InMemoryRecurringScheduleRecord(
  string scheduleKey,
  string cronExpression,
  string serviceType,
  string methodName,
  IReadOnlyList<string> methodParameterTypes,
  SerializedJobPayload serializedArguments,
  int priority,
  IReadOnlyList<string> concurrencyGroupKeys,
  RetryPolicy? retryPolicy,
  RecurringOverlapMode overlapMode,
  bool isPaused,
  DateTimeOffset? nextFireAtUtc)
{
    public string ScheduleKey { get; } = scheduleKey;

    public string CronExpression { get; set; } = cronExpression;

    public string ServiceType { get; set; } = serviceType;

    public string MethodName { get; set; } = methodName;

    public IReadOnlyList<string> MethodParameterTypes { get; set; } = methodParameterTypes;

    public SerializedJobPayload SerializedArguments { get; set; } = serializedArguments;

    public int Priority { get; set; } = priority;

    public IReadOnlyList<string> ConcurrencyGroupKeys { get; set; } = concurrencyGroupKeys;

    public RetryPolicy? RetryPolicy { get; set; } = retryPolicy;

    public RecurringOverlapMode OverlapMode { get; set; } = overlapMode;

    public bool IsPaused { get; set; } = isPaused;

    public DateTimeOffset? NextFireAtUtc { get; set; } = nextFireAtUtc;
}
