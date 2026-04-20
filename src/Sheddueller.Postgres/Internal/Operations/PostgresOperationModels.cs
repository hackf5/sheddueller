namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed record PostgresClaimedJob(
    Guid JobId,
    int AttemptCount,
    int MaxAttempts,
    RetryBackoffKind? RetryBackoffKind,
    TimeSpan? RetryBaseDelay,
    TimeSpan? RetryMaxDelay,
    IReadOnlyList<string> GroupKeys);

internal sealed record PostgresRetryPolicy(
    bool IsConfigured,
    int MaxAttempts,
    RetryBackoffKind? BackoffKind,
    TimeSpan? BaseDelay,
    TimeSpan? MaxDelay);

internal sealed record PostgresScheduleDefinition(
    string ScheduleKey,
    string CronExpression,
    bool IsPaused,
    RecurringOverlapMode OverlapMode,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedJobPayload SerializedArguments,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    RetryPolicy? RetryPolicy,
    DateTimeOffset? NextFireAtUtc)
{
    public bool EqualsRequest(UpsertRecurringScheduleRequest request)
      => string.Equals(this.CronExpression, request.CronExpression, StringComparison.Ordinal)
        && this.OverlapMode == request.OverlapMode
        && this.Priority == request.Priority
        && string.Equals(this.ServiceType, request.ServiceType, StringComparison.Ordinal)
        && string.Equals(this.MethodName, request.MethodName, StringComparison.Ordinal)
        && this.MethodParameterTypes.SequenceEqual(request.MethodParameterTypes, StringComparer.Ordinal)
        && string.Equals(this.SerializedArguments.ContentType, request.SerializedArguments.ContentType, StringComparison.Ordinal)
        && this.SerializedArguments.Data.SequenceEqual(request.SerializedArguments.Data)
        && this.ConcurrencyGroupKeys.SequenceEqual(request.ConcurrencyGroupKeys, StringComparer.Ordinal)
        && this.RetryPolicy == request.RetryPolicy;
}
