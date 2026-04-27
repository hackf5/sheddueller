namespace Sheddueller.Testing.Tests;

using Microsoft.Extensions.Time.Testing;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class FakeRecurringScheduleManagerTests
{
    [Fact]
    public async Task CreateOrUpdate_ServiceMethodCall_RecordsScheduleAndMatchesByKeyAndExpression()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero));
        var fake = new FakeRecurringScheduleManager(new SystemTextJsonJobPayloadSerializer(), timeProvider);
        var payload = new SamplePayload("alpha", 42);
        var options = new RecurringScheduleOptions(
          Priority: 7,
          ConcurrencyGroupKeys: ["group-a", "group-a"],
          RetryPolicy: new RetryPolicy(3, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2)),
          OverlapMode: RecurringOverlapMode.Allow,
          Tags: [new JobTag(" tenant ", " acme ")]);

        var result = await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleAsync(payload, ct),
          options);

        var match = await fake.MatchAsync<TestScheduleService>(
          "schedule-a",
          (s, ct) => s.HandleAsync(new SamplePayload("alpha", 42), ct));

        result.ShouldBe(RecurringScheduleUpsertResult.Created);
        match.Count.ShouldBe(1);
        match[0].ScheduleKey.ShouldBe("schedule-a");
        match[0].CronExpression.ShouldBe("* * * * *");
        match[0].Priority.ShouldBe(7);
        match[0].ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        match[0].RetryPolicy.ShouldBe(new RetryPolicy(3, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2)));
        match[0].OverlapMode.ShouldBe(RecurringOverlapMode.Allow);
        match[0].Tags.ShouldBe([new JobTag("tenant", "acme")]);
        match[0].NextFireAtUtc.ShouldBe(new DateTimeOffset(2026, 4, 19, 10, 31, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Match_DifferentKeyOrSerializedArguments_ReturnsEmptyMatch()
    {
        var fake = new FakeRecurringScheduleManager();

        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleAsync(new SamplePayload("alpha", 42), ct));

        (await fake.MatchAsync<TestScheduleService>(
          "schedule-b",
          (s, ct) => s.HandleAsync(new SamplePayload("alpha", 42), ct))).ShouldBeEmpty();
        (await fake.MatchAsync<TestScheduleService>(
          "schedule-a",
            (s, ct) => s.HandleAsync(new SamplePayload("alpha", 43), ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateOrUpdate_ProgressAwareServiceMethod_RecordsScheduleAndMatchesByExpression()
    {
        var fake = new FakeRecurringScheduleManager();
        var payload = new SamplePayload("alpha", 42);

        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct, progress) => s.HandleWithProgressAsync(payload, progress, ct));

        var match = await fake.MatchAsync<TestScheduleService>(
          "schedule-a",
          (s, ct, progress) => s.HandleWithProgressAsync(new SamplePayload("alpha", 42), progress, ct));

        match.Count.ShouldBe(1);
        match[0].MethodParameterTypes.ShouldBe([typeof(SamplePayload), typeof(IProgress<decimal>), typeof(CancellationToken)]);
        match[0].MethodParameterBindings.ShouldBe([
          new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
          new JobMethodParameterBinding(JobMethodParameterBindingKind.ProgressReporter),
          new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken),
        ]);
    }

    [Fact]
    public async Task CreateOrUpdate_DefinitionChanges_ReturnsCreatedUnchangedAndUpdated()
    {
        var fake = new FakeRecurringScheduleManager();

        var created = await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));
        var unchanged = await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));
        var updated = await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "*/5 * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));

        created.ShouldBe(RecurringScheduleUpsertResult.Created);
        unchanged.ShouldBe(RecurringScheduleUpsertResult.Unchanged);
        updated.ShouldBe(RecurringScheduleUpsertResult.Updated);
        fake.Schedules[0].CronExpression.ShouldBe("*/5 * * * *");
    }

    [Fact]
    public async Task PauseResumeDeleteGetAndList_ExistingSchedule_UpdatesCurrentState()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero));
        var fake = new FakeRecurringScheduleManager(new SystemTextJsonJobPayloadSerializer(), timeProvider);

        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-b",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("beta", ct));
        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));

        (await fake.PauseAsync("schedule-a")).ShouldBeTrue();
        (await fake.GetAsync("schedule-a")).ShouldNotBeNull().IsPaused.ShouldBeTrue();
        (await fake.GetAsync("schedule-a")).ShouldNotBeNull().NextFireAtUtc.ShouldBeNull();

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        (await fake.ResumeAsync("schedule-a")).ShouldBeTrue();
        (await fake.GetAsync("schedule-a")).ShouldNotBeNull().IsPaused.ShouldBeFalse();
        (await fake.GetAsync("schedule-a")).ShouldNotBeNull().NextFireAtUtc.ShouldBe(new DateTimeOffset(2026, 4, 19, 10, 41, 0, TimeSpan.Zero));

        var listed = await ListAsync(fake);
        listed.Select(schedule => schedule.ScheduleKey).ShouldBe(["schedule-a", "schedule-b"]);

        (await fake.DeleteAsync("schedule-a")).ShouldBeTrue();
        (await fake.GetAsync("schedule-a")).ShouldBeNull();
        (await fake.DeleteAsync("schedule-a")).ShouldBeFalse();
        (await fake.PauseAsync("schedule-a")).ShouldBeFalse();
        (await fake.ResumeAsync("schedule-a")).ShouldBeFalse();
    }

    [Fact]
    public async Task Trigger_ExistingSchedule_RecordsTriggeredJobFromTemplate()
    {
        var fake = new FakeRecurringScheduleManager();
        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct),
          new RecurringScheduleOptions(
            Priority: 7,
            ConcurrencyGroupKeys: ["group-a"],
            RetryPolicy: new RetryPolicy(3, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2)),
            OverlapMode: RecurringOverlapMode.Allow,
            Tags: [new JobTag("tenant", "acme")]));

        var result = await fake.TriggerAsync("schedule-a");

        result.Status.ShouldBe(RecurringScheduleTriggerStatus.Enqueued);
        result.JobId.ShouldNotBeNull();
        result.EnqueueSequence.ShouldBe(0);
        fake.TriggeredJobs.Count.ShouldBe(1);
        fake.TriggeredJobs[0].JobId.ShouldBe(result.JobId.Value);
        fake.TriggeredJobs[0].SourceScheduleKey.ShouldBe("schedule-a");
        fake.TriggeredJobs[0].Priority.ShouldBe(7);
        fake.TriggeredJobs[0].ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        fake.TriggeredJobs[0].RetryPolicy.ShouldBe(new RetryPolicy(3, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2)));
        fake.TriggeredJobs[0].Tags.ShouldBe([new JobTag("tenant", "acme")]);
    }

    [Fact]
    public async Task Trigger_MissingPausedSkipAndAllowSchedules_ReturnsMatchingStatus()
    {
        var fake = new FakeRecurringScheduleManager();
        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "skip-schedule",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("skip", ct));
        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "allow-schedule",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("allow", ct),
          new RecurringScheduleOptions(OverlapMode: RecurringOverlapMode.Allow));
        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "paused-schedule",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("paused", ct));
        await fake.PauseAsync("paused-schedule");

        (await fake.TriggerAsync("missing")).Status.ShouldBe(RecurringScheduleTriggerStatus.NotFound);
        (await fake.TriggerAsync("paused-schedule")).Status.ShouldBe(RecurringScheduleTriggerStatus.Enqueued);
        (await fake.TriggerAsync("skip-schedule")).Status.ShouldBe(RecurringScheduleTriggerStatus.Enqueued);
        (await fake.TriggerAsync("skip-schedule")).Status.ShouldBe(RecurringScheduleTriggerStatus.SkippedActiveOccurrence);
        (await fake.TriggerAsync("allow-schedule")).Status.ShouldBe(RecurringScheduleTriggerStatus.Enqueued);
        (await fake.TriggerAsync("allow-schedule")).Status.ShouldBe(RecurringScheduleTriggerStatus.Enqueued);
        fake.TriggeredJobs.Select(job => job.SourceScheduleKey).ShouldBe([
            "paused-schedule",
            "skip-schedule",
            "allow-schedule",
            "allow-schedule",
        ]);
    }

    [Fact]
    public async Task Delete_ExistingSchedule_RemovesScheduleFromMatches()
    {
        var fake = new FakeRecurringScheduleManager();

        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));

        await fake.DeleteAsync("schedule-a");

        (await fake.MatchAsync<TestScheduleService>(
          "schedule-a",
          (s, ct) => s.HandleStringAsync("alpha", ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Match_CustomSerializer_MatchesBySerializedPayload()
    {
        var fake = new FakeRecurringScheduleManager(new ConstantPayloadSerializer());

        await fake.CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleAsync(new SamplePayload("alpha", 42), ct));

        var match = await fake.MatchAsync<TestScheduleService>(
          "schedule-a",
          (s, ct) => s.HandleAsync(new SamplePayload("beta", 99), ct));

        match.Count.ShouldBe(1);
    }

    private static async Task<IReadOnlyList<RecurringScheduleInfo>> ListAsync(
      FakeRecurringScheduleManager manager)
    {
        var schedules = new List<RecurringScheduleInfo>();
        await foreach (var schedule in manager.ListAsync())
        {
            schedules.Add(schedule);
        }

        return schedules;
    }

    private sealed record SamplePayload(string Name, int Count);

    private sealed class TestScheduleService
    {
        public Task HandleAsync(SamplePayload payload, CancellationToken cancellationToken)
          => Task.CompletedTask;

        public Task HandleStringAsync(string value, CancellationToken cancellationToken)
          => Task.CompletedTask;

        public Task HandleWithProgressAsync(SamplePayload payload, IProgress<decimal> progress, CancellationToken cancellationToken)
          => Task.CompletedTask;
    }

    private sealed class ConstantPayloadSerializer : IJobPayloadSerializer
    {
        public ValueTask<SerializedJobPayload> SerializeAsync(
          IReadOnlyList<object?> arguments,
          IReadOnlyList<Type> parameterTypes,
          CancellationToken cancellationToken = default)
          => ValueTask.FromResult(new SerializedJobPayload("test/constant", [1, 2, 3]));

        public ValueTask<IReadOnlyList<object?>> DeserializeAsync(
          SerializedJobPayload payload,
          IReadOnlyList<Type> parameterTypes,
          CancellationToken cancellationToken = default)
          => ValueTask.FromResult<IReadOnlyList<object?>>([]);
    }
}
