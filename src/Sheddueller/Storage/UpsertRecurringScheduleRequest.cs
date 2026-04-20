namespace Sheddueller.Storage;

using Sheddueller.Serialization;

/// <summary>
/// Store request for creating or updating a recurring schedule.
/// </summary>
public sealed record UpsertRecurringScheduleRequest(
    string ScheduleKey,
    string CronExpression,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    SerializedJobPayload SerializedArguments,
    int Priority,
    IReadOnlyList<string> ConcurrencyGroupKeys,
    RetryPolicy? RetryPolicy,
    RecurringOverlapMode OverlapMode,
    DateTimeOffset UpsertedAtUtc);
