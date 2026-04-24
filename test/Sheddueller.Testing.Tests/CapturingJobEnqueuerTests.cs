namespace Sheddueller.Testing.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Serialization;

using Shouldly;

public sealed class CapturingJobEnqueuerTests
{
    [Fact]
    public async Task Capture_EnqueuedJob_RecordsThroughInjectedInterface()
    {
        var capturingEnqueuer = new CapturingJobEnqueuer(new SystemTextJsonJobPayloadSerializer());
        var payload = new SamplePayload("alpha", 42);

        using var capture = capturingEnqueuer.Capture();

        await ((IJobEnqueuer)capturingEnqueuer).EnqueueAsync<TestJobService>(
          (s, ct) => s.HandleAsync(payload, ct));

        var match = await capture.Fake.MatchAsync<TestJobService>(
          (s, ct) => s.HandleAsync(new SamplePayload("alpha", 42), ct));

        match.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Capture_AwaitBoundary_FlowsCurrentCapture()
    {
        var capturingEnqueuer = new CapturingJobEnqueuer(new SystemTextJsonJobPayloadSerializer());

        using var capture = capturingEnqueuer.Capture();

        await Task.Yield();
        await ((IJobEnqueuer)capturingEnqueuer).EnqueueAsync<TestJobService>(
          (s, ct) => s.HandleStringAsync("alpha", ct));

        var match = await capture.Fake.MatchAsync<TestJobService>(
          (s, ct) => s.HandleStringAsync("alpha", ct));

        match.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Enqueue_NoActiveCapture_SucceedsAndDiscardsRecording()
    {
        var capturingEnqueuer = new CapturingJobEnqueuer(new SystemTextJsonJobPayloadSerializer());

        var jobId = await ((IJobEnqueuer)capturingEnqueuer).EnqueueAsync<TestJobService>(
          (s, ct) => s.HandleStringAsync("discarded", ct));

        using var capture = capturingEnqueuer.Capture();
        var match = await capture.Fake.MatchAsync<TestJobService>(
          (s, ct) => s.HandleStringAsync("discarded", ct));

        jobId.ShouldNotBe(Guid.Empty);
        match.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enqueue_NoActiveCapture_InvalidExpressionStillThrows()
    {
        var capturingEnqueuer = new CapturingJobEnqueuer(new SystemTextJsonJobPayloadSerializer());

        await Should.ThrowAsync<ArgumentException>(
          () => ((IJobEnqueuer)capturingEnqueuer).EnqueueAsync<TestJobService>((s, ct) => s.NoTokenAsync()).AsTask());
    }

    [Fact]
    public void Capture_AlreadyActive_Throws()
    {
        var capturingEnqueuer = new CapturingJobEnqueuer(new SystemTextJsonJobPayloadSerializer());

        using var capture = capturingEnqueuer.Capture();

        Should.Throw<InvalidOperationException>(capturingEnqueuer.Capture);
    }

    [Fact]
    public async Task Capture_Disposed_ClearsCurrentCapture()
    {
        var capturingEnqueuer = new CapturingJobEnqueuer(new SystemTextJsonJobPayloadSerializer());
        var capture = capturingEnqueuer.Capture();

        capture.Dispose();
        capture.Dispose();

        await ((IJobEnqueuer)capturingEnqueuer).EnqueueAsync<TestJobService>(
          (s, ct) => s.HandleStringAsync("discarded", ct));

        using var nextCapture = capturingEnqueuer.Capture();
        var match = await nextCapture.Fake.MatchAsync<TestJobService>(
          (s, ct) => s.HandleStringAsync("discarded", ct));

        match.ShouldBeEmpty();
    }

    [Fact]
    public void AddShedduellerTesting_AfterAddSheddueller_ReplacesJobEnqueuer()
    {
        var services = new ServiceCollection();
        services.AddSheddueller();

        services.AddShedduellerTesting();

        using var provider = services.BuildServiceProvider();
        var capturingEnqueuer = provider.GetRequiredService<CapturingJobEnqueuer>();

        provider.GetRequiredService<IJobEnqueuer>().ShouldBeSameAs(capturingEnqueuer);
    }

    [Fact]
    public void AddShedduellerTesting_BeforeAddSheddueller_KeepsCapturingJobEnqueuer()
    {
        var services = new ServiceCollection();
        services.AddShedduellerTesting();

        services.AddSheddueller();

        using var provider = services.BuildServiceProvider();
        var capturingEnqueuer = provider.GetRequiredService<CapturingJobEnqueuer>();

        provider.GetRequiredService<IJobEnqueuer>().ShouldBeSameAs(capturingEnqueuer);
    }

    private sealed record SamplePayload(string Name, int Count);

    private sealed class TestJobService
    {
        public Task HandleAsync(SamplePayload payload, CancellationToken cancellationToken)
          => Task.CompletedTask;

        public Task HandleStringAsync(string value, CancellationToken cancellationToken)
          => Task.CompletedTask;

        public Task NoTokenAsync()
          => Task.CompletedTask;
    }
}
