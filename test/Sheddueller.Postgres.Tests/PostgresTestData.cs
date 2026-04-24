namespace Sheddueller.Postgres.Tests;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

internal static class PostgresTestData
{
    public static string ServiceType { get; } = typeof(PostgresTestService).AssemblyQualifiedName!;

    public static string MethodName { get; } = nameof(PostgresTestService.RunAsync);

    public static async ValueTask<ClaimedJob> ClaimAsync(
        IJobStore store,
        string nodeId = "node-1",
        TimeSpan? leaseDuration = null)
    {
        var now = DateTimeOffset.UtcNow;
        return (await store.TryClaimNextAsync(new ClaimJobRequest(nodeId, now, now.Add(leaseDuration ?? TimeSpan.FromSeconds(30)))))
          .ShouldBeOfType<ClaimJobResult.Claimed>()
          .Job;
    }

    public static ClaimJobRequest ClaimRequest(
        string nodeId = "node-1",
        TimeSpan? leaseDuration = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ClaimJobRequest(nodeId, now, now.Add(leaseDuration ?? TimeSpan.FromSeconds(30)));
    }

    public static EnqueueJobRequest CreateRequest(
        Guid jobId,
        int priority = 0,
        DateTimeOffset? enqueuedAtUtc = null,
        DateTimeOffset? notBeforeUtc = null,
        int maxAttempts = 1,
        RetryBackoffKind? retryBackoffKind = null,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null,
        IReadOnlyList<string>? groupKeys = null,
        IReadOnlyList<JobTag>? tags = null,
        JobInvocationTargetKind invocationTargetKind = JobInvocationTargetKind.Instance,
        IReadOnlyList<JobMethodParameterBinding>? methodParameterBindings = null,
        string? idempotencyKey = null)
      => new(
        jobId,
        priority,
        ServiceType,
        MethodName,
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        groupKeys ?? [],
        enqueuedAtUtc ?? DateTimeOffset.UtcNow,
        notBeforeUtc,
        maxAttempts,
        retryBackoffKind,
        retryBaseDelay,
        retryMaxDelay,
        Tags: tags,
        InvocationTargetKind: invocationTargetKind,
        MethodParameterBindings: methodParameterBindings,
        IdempotencyKey: idempotencyKey);

    public static UpsertRecurringScheduleRequest CreateSchedule(
        string scheduleKey,
        int priority = 0,
        IReadOnlyList<string>? groupKeys = null,
        RetryPolicy? retryPolicy = null,
        RecurringOverlapMode overlapMode = RecurringOverlapMode.Skip,
        JobInvocationTargetKind invocationTargetKind = JobInvocationTargetKind.Instance,
        IReadOnlyList<JobMethodParameterBinding>? methodParameterBindings = null)
      => new(
        scheduleKey,
        "* * * * *",
        ServiceType,
        MethodName,
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        priority,
        groupKeys ?? [],
        retryPolicy,
        overlapMode,
        DateTimeOffset.UtcNow,
        InvocationTargetKind: invocationTargetKind,
        MethodParameterBindings: methodParameterBindings);

    public static JobFailureInfo CreateFailure()
      => new("TestException", "failed", "stack");

    public static SerializedJobPayload EmptyPayload()
      => new(SystemTextJsonJobPayloadSerializer.JsonContentType, "[]"u8.ToArray());

    private sealed class PostgresTestService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
