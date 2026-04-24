namespace Sheddueller.Testing.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Sheddueller.Serialization;

using Shouldly;

public sealed class CapturingRecurringScheduleManagerTests
{
    [Fact]
    public async Task Capture_RecurringSchedule_RecordsThroughInjectedInterface()
    {
        var capturingManager = CreateCapturingManager();

        using var capture = capturingManager.Capture();

        await ((IRecurringScheduleManager)capturingManager).CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));

        var match = await capture.Fake.MatchAsync<TestScheduleService>(
          "schedule-a",
          (s, ct) => s.HandleStringAsync("alpha", ct));

        match.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Capture_AwaitBoundary_FlowsCurrentCapture()
    {
        var capturingManager = CreateCapturingManager();

        using var capture = capturingManager.Capture();

        await Task.Yield();
        await ((IRecurringScheduleManager)capturingManager).CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("alpha", ct));

        (await capture.Fake.GetAsync("schedule-a")).ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateOrUpdate_NoActiveCapture_SucceedsAndDiscardsRecording()
    {
        var capturingManager = CreateCapturingManager();

        var result = await ((IRecurringScheduleManager)capturingManager).CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("discarded", ct));

        using var capture = capturingManager.Capture();
        var match = await capture.Fake.MatchAsync<TestScheduleService>(
          "schedule-a",
          (s, ct) => s.HandleStringAsync("discarded", ct));

        result.ShouldBe(RecurringScheduleUpsertResult.Created);
        match.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoActiveCapture_ReadAndCommands_ReturnDefaultEmptyState()
    {
        var capturingManager = CreateCapturingManager();

        (await capturingManager.DeleteAsync("schedule-a")).ShouldBeFalse();
        (await capturingManager.PauseAsync("schedule-a")).ShouldBeFalse();
        (await capturingManager.ResumeAsync("schedule-a")).ShouldBeFalse();
        (await capturingManager.GetAsync("schedule-a")).ShouldBeNull();
        (await ListAsync(capturingManager)).ShouldBeEmpty();
    }

    [Fact]
    public async Task NoActiveCapture_InvalidExpressionStillThrows()
    {
        var capturingManager = CreateCapturingManager();

        await Should.ThrowAsync<ArgumentException>(
          () => ((IRecurringScheduleManager)capturingManager)
            .CreateOrUpdateAsync<TestScheduleService>("schedule-a", "* * * * *", (s, ct) => s.NoTokenAsync())
            .AsTask());
    }

    [Fact]
    public void Capture_AlreadyActive_Throws()
    {
        var capturingManager = CreateCapturingManager();

        using var capture = capturingManager.Capture();

        Should.Throw<InvalidOperationException>(capturingManager.Capture);
    }

    [Fact]
    public async Task Capture_Disposed_ClearsCurrentCapture()
    {
        var capturingManager = CreateCapturingManager();
        var capture = capturingManager.Capture();

        capture.Dispose();
        capture.Dispose();

        await ((IRecurringScheduleManager)capturingManager).CreateOrUpdateAsync<TestScheduleService>(
          "schedule-a",
          "* * * * *",
          (s, ct) => s.HandleStringAsync("discarded", ct));

        using var nextCapture = capturingManager.Capture();

        (await nextCapture.Fake.GetAsync("schedule-a")).ShouldBeNull();
    }

    [Fact]
    public void AddShedduellerTesting_AfterAddSheddueller_ReplacesRecurringScheduleManager()
    {
        var services = new ServiceCollection();
        services.AddSheddueller();

        services.AddShedduellerTesting();

        using var provider = services.BuildServiceProvider();
        var capturingManager = provider.GetRequiredService<CapturingRecurringScheduleManager>();

        provider.GetRequiredService<IRecurringScheduleManager>().ShouldBeSameAs(capturingManager);
    }

    [Fact]
    public void AddShedduellerTesting_BeforeAddSheddueller_KeepsCapturingRecurringScheduleManager()
    {
        var services = new ServiceCollection();
        services.AddShedduellerTesting();

        services.AddSheddueller();

        using var provider = services.BuildServiceProvider();
        var capturingManager = provider.GetRequiredService<CapturingRecurringScheduleManager>();

        provider.GetRequiredService<IRecurringScheduleManager>().ShouldBeSameAs(capturingManager);
    }

    private static CapturingRecurringScheduleManager CreateCapturingManager()
      => new(
        new SystemTextJsonJobPayloadSerializer(),
        new FakeTimeProvider(new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero)));

    private static async Task<IReadOnlyList<RecurringScheduleInfo>> ListAsync(
      CapturingRecurringScheduleManager manager)
    {
        var schedules = new List<RecurringScheduleInfo>();
        await foreach (var schedule in manager.ListAsync())
        {
            schedules.Add(schedule);
        }

        return schedules;
    }

    private sealed class TestScheduleService
    {
        public Task HandleStringAsync(string value, CancellationToken cancellationToken)
          => Task.CompletedTask;

        public Task NoTokenAsync()
          => Task.CompletedTask;
    }
}
