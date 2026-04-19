namespace Sheddueller.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class V1TaskEnqueuerTests
{
    [Fact]
    public async Task EnqueueAsyncPersistsMethodIdentityAndSerializedArgumentsWithoutCancellationToken()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero);
        using var provider = CreateProvider(new ManualTimeProvider(timestamp));
        var enqueuer = provider.GetRequiredService<ITaskEnqueuer>();
        var store = provider.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();
        var payload = new SamplePayload("alpha", 42);

        var taskId = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          new TaskSubmission(7, ["group-a", "group-a"]));

        var snapshot = store.GetSnapshot(taskId).ShouldNotBeNull();
        snapshot.State.ShouldBe(TaskState.Queued);
        snapshot.Priority.ShouldBe(7);
        snapshot.EnqueuedAtUtc.ShouldBe(timestamp);
        snapshot.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        snapshot.MethodName.ShouldBe(nameof(EnqueueTestService.HandleAsync));
        snapshot.ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        snapshot.MethodParameterTypes.ShouldBe([
          typeof(SamplePayload).AssemblyQualifiedName!,
      typeof(CancellationToken).AssemblyQualifiedName!,
    ]);

        var arguments = await provider.GetRequiredService<ITaskPayloadSerializer>()
          .DeserializeAsync(snapshot.SerializedArguments, [typeof(SamplePayload)]);

        arguments.Count.ShouldBe(1);
        arguments[0].ShouldBe(payload);
    }

    [Fact]
    public async Task EnqueueAsyncEvaluatesArgumentSubexpressionsExactlyOnce()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<ITaskEnqueuer>();
        var valueFactory = new CountingValueFactory();

        await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleStringAsync(valueFactory.NextValue(), cancellationToken));

        valueFactory.EvaluationCount.ShouldBe(1);
    }

    [Fact]
    public async Task EnqueueAsyncRejectsUnsupportedExpressionForms()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<ITaskEnqueuer>();
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
}
