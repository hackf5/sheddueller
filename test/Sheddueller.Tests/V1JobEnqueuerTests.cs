namespace Sheddueller.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class V1JobEnqueuerTests
{
    [Fact]
    public async Task Enqueue_ServiceMethodCall_PersistsMethodIdentityAndArgumentsWithoutCancellationToken()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero);
        using var provider = CreateProvider(new ManualTimeProvider(timestamp));
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        var payload = new SamplePayload("alpha", 42);

        var jobId = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          new JobSubmission(7, ["group-a", "group-a"]));

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.State.ShouldBe(JobState.Queued);
        snapshot.Priority.ShouldBe(7);
        snapshot.EnqueuedAtUtc.ShouldBe(timestamp);
        snapshot.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        snapshot.MethodName.ShouldBe(nameof(EnqueueTestService.HandleAsync));
        snapshot.ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        snapshot.MethodParameterTypes.ShouldBe([
          typeof(SamplePayload).AssemblyQualifiedName!,
      typeof(CancellationToken).AssemblyQualifiedName!,
    ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(snapshot.SerializedArguments, [typeof(SamplePayload)]);

        arguments.Count.ShouldBe(1);
        arguments[0].ShouldBe(payload);
    }

    [Fact]
    public async Task Enqueue_JobContextAwareMethod_PersistsMethodIdentityWithoutSerializingContext()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        var payload = new SamplePayload("alpha", 42);

        var jobId = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleWithContextAsync(payload, Job.Context, cancellationToken));

        var snapshot = store.GetSnapshot(jobId).ShouldNotBeNull();
        snapshot.MethodParameterTypes.ShouldBe([
          typeof(SamplePayload).AssemblyQualifiedName!,
          typeof(IJobContext).AssemblyQualifiedName!,
          typeof(CancellationToken).AssemblyQualifiedName!,
        ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(snapshot.SerializedArguments, [typeof(SamplePayload)]);

        arguments.Count.ShouldBe(1);
        arguments[0].ShouldBe(payload);
    }

    [Fact]
    public async Task Enqueue_ArgumentSubexpressions_EvaluatesExactlyOnce()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var valueFactory = new CountingValueFactory();

        await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleStringAsync(valueFactory.NextValue(), cancellationToken));

        valueFactory.EvaluationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Enqueue_UnsupportedExpressionForms_ThrowsArgumentException()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        using var externalCancellationTokenSource = new CancellationTokenSource();
        var externalCancellationToken = externalCancellationTokenSource.Token;
        var payload = new SamplePayload("alpha", 42);

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (_, cancellationToken) => EnqueueTestService.StaticAsync(cancellationToken)).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.GenericAsync(123, cancellationToken)).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, _) => service.HandleAsync(payload, CancellationToken.None)).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, _) => service.NoTokenAsync()).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.HandleObjectAsync(externalCancellationToken, cancellationToken)).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.HandleWithContextAsync(payload, new StubJobContext(), cancellationToken)).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.HandleObjectAsync(Job.Context, cancellationToken)).AsTask());
    }

    private static ServiceProvider CreateProvider(TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        services.AddSheddueller(builder => builder.UseInMemoryStore());
        services.AddTransient<EnqueueTestService>();

        return services.BuildServiceProvider();
    }

    private sealed record SamplePayload(string Name, int Count);

    private sealed class CountingValueFactory
    {
        public int EvaluationCount { get; private set; }

        public string NextValue()
        {
            EvaluationCount++;
            return "captured";
        }
    }

    private sealed class EnqueueTestService
    {
        public Task HandleAsync(SamplePayload payload, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task HandleStringAsync(string value, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task HandleWithContextAsync(SamplePayload payload, IJobContext jobContext, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task HandleObjectAsync(object value, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task NoTokenAsync()
        {
            return Task.CompletedTask;
        }

        public Task GenericAsync<TValue>(TValue value, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public static Task StaticAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubJobContext : IJobContext
    {
        public Guid JobId => Guid.Empty;

        public int AttemptNumber => 0;

        public CancellationToken CancellationToken => CancellationToken.None;

        public ValueTask LogAsync(
            JobLogLevel level,
            string message,
            IReadOnlyDictionary<string, string>? fields = null,
            CancellationToken cancellationToken = default)
          => ValueTask.CompletedTask;

        public ValueTask ReportProgressAsync(
            double? percent,
            string? message = null,
            CancellationToken cancellationToken = default)
          => ValueTask.CompletedTask;
    }
}
