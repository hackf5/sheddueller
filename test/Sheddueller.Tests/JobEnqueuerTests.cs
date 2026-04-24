namespace Sheddueller.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class JobEnqueuerTests
{
    [Fact]
    public async Task Enqueue_ServiceMethodCall_PersistsMethodIdentityAndArgumentsWithoutCancellationToken()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero);
        using var provider = CreateProvider(new FakeTimeProvider(timestamp));
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("alpha", 42);

        var jobId = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          new JobSubmission(7, ["group-a", "group-a"]));

        var request = store.GetRequest(jobId);
        request.Priority.ShouldBe(7);
        request.EnqueuedAtUtc.ShouldBe(timestamp);
        request.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        request.MethodName.ShouldBe(nameof(EnqueueTestService.HandleAsync));
        request.ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        request.MethodParameterTypes.ShouldBe([
          typeof(SamplePayload).AssemblyQualifiedName!,
          typeof(CancellationToken).AssemblyQualifiedName!,
        ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(request.SerializedArguments, [typeof(SamplePayload)]);

        arguments.Count.ShouldBe(1);
        arguments[0].ShouldBe(payload);
    }

    [Fact]
    public async Task Enqueue_JobContextAwareMethod_PersistsMethodIdentityWithoutSerializingContext()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("alpha", 42);

        var jobId = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleWithContextAsync(payload, Job.Context, cancellationToken));

        var request = store.GetRequest(jobId);
        request.MethodParameterTypes.ShouldBe([
          typeof(SamplePayload).AssemblyQualifiedName!,
          typeof(IJobContext).AssemblyQualifiedName!,
          typeof(CancellationToken).AssemblyQualifiedName!,
        ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(request.SerializedArguments, [typeof(SamplePayload)]);

        arguments.Count.ShouldBe(1);
        arguments[0].ShouldBe(payload);
    }

    [Fact]
    public async Task Enqueue_StaticMethodCall_PersistsStaticInvocationMetadata()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("static", 7);

        var jobId = await enqueuer.EnqueueAsync(
          cancellationToken => EnqueueTestService.StaticWithPayloadAsync(payload, cancellationToken));

        var request = store.GetRequest(jobId);
        request.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        request.MethodName.ShouldBe(nameof(EnqueueTestService.StaticWithPayloadAsync));
        request.InvocationTargetKind.ShouldBe(JobInvocationTargetKind.Static);
        request.MethodParameterBindings.ShouldBe([
          new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
          new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken),
        ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(request.SerializedArguments, [typeof(SamplePayload)]);

        arguments.ShouldBe([payload]);
    }

    [Fact]
    public async Task Enqueue_ResolvedServiceTarget_PersistsInstanceInvocationMetadata()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("resolved", 11);

        var jobId = await enqueuer.EnqueueAsync(
          cancellationToken => Job.Resolve<EnqueueTestService>().HandleAsync(payload, cancellationToken));

        var request = store.GetRequest(jobId);
        request.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        request.MethodName.ShouldBe(nameof(EnqueueTestService.HandleAsync));
        request.InvocationTargetKind.ShouldBe(JobInvocationTargetKind.Instance);
        request.MethodParameterBindings.ShouldBe([
          new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
          new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken),
        ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(request.SerializedArguments, [typeof(SamplePayload)]);

        arguments.ShouldBe([payload]);
    }

    [Fact]
    public async Task Enqueue_ResolvedServiceArgument_PersistsServiceBindingWithoutSerializingArgument()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();

        var jobId = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleDependencyAsync(Job.Resolve<ResolvedDependency>(), cancellationToken));

        var request = store.GetRequest(jobId);
        request.MethodParameterTypes.ShouldBe([
          typeof(ResolvedDependency).AssemblyQualifiedName!,
          typeof(CancellationToken).AssemblyQualifiedName!,
        ]);
        request.MethodParameterBindings.ShouldBe([
          new JobMethodParameterBinding(JobMethodParameterBindingKind.Service, typeof(ResolvedDependency).AssemblyQualifiedName),
          new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken),
        ]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(request.SerializedArguments, []);

        arguments.ShouldBeEmpty();
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
    public async Task EnqueueMany_MixedServiceMethods_PersistsJobsAndReturnsIdsInInputOrder()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero);
        var notBeforeUtc = timestamp.AddMinutes(5);
        using var provider = CreateProvider(new FakeTimeProvider(timestamp));
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("alpha", 42);
        var retryPolicy = new RetryPolicy(3, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(9));

        var jobIds = await enqueuer.EnqueueManyAsync([
          JobEnqueueItem.Create<EnqueueTestService>(
            (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
            new JobSubmission(
              Priority: 7,
              ConcurrencyGroupKeys: ["group-a", "group-a"],
              NotBeforeUtc: notBeforeUtc,
              RetryPolicy: retryPolicy,
              Tags: [new JobTag("tenant", "acme")])),
          JobEnqueueItem.Create<SecondEnqueueTestService>(
            (service, cancellationToken) => service.HandleValueTaskAsync("beta", cancellationToken),
            new JobSubmission(
              Priority: -1,
              Tags: [new JobTag("kind", "secondary")])),
          JobEnqueueItem.Create(
            cancellationToken => EnqueueTestService.StaticWithPayloadAsync(payload, cancellationToken),
            new JobSubmission(Priority: 3)),
        ]);

        jobIds.Count.ShouldBe(3);
        jobIds[0].ShouldNotBe(Guid.Empty);
        jobIds[1].ShouldNotBe(Guid.Empty);
        jobIds[2].ShouldNotBe(Guid.Empty);
        jobIds[1].ShouldNotBe(jobIds[0]);
        jobIds[2].ShouldNotBe(jobIds[0]);
        jobIds[2].ShouldNotBe(jobIds[1]);

        var firstRequest = store.GetRequest(jobIds[0]);
        firstRequest.Priority.ShouldBe(7);
        firstRequest.EnqueuedAtUtc.ShouldBe(timestamp);
        firstRequest.NotBeforeUtc.ShouldBe(notBeforeUtc);
        firstRequest.MaxAttempts.ShouldBe(3);
        firstRequest.RetryBackoffKind.ShouldBe(RetryBackoffKind.Fixed);
        firstRequest.RetryBaseDelay.ShouldBe(TimeSpan.FromSeconds(2));
        firstRequest.RetryMaxDelay.ShouldBe(TimeSpan.FromSeconds(9));
        firstRequest.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        firstRequest.MethodName.ShouldBe(nameof(EnqueueTestService.HandleAsync));
        firstRequest.ConcurrencyGroupKeys.ShouldBe(["group-a"]);
        firstRequest.Tags.ShouldBe([new JobTag("tenant", "acme")]);

        var secondRequest = store.GetRequest(jobIds[1]);
        secondRequest.Priority.ShouldBe(-1);
        secondRequest.EnqueuedAtUtc.ShouldBe(timestamp);
        secondRequest.ServiceType.ShouldBe(typeof(SecondEnqueueTestService).AssemblyQualifiedName);
        secondRequest.MethodName.ShouldBe(nameof(SecondEnqueueTestService.HandleValueTaskAsync));
        secondRequest.Tags.ShouldBe([new JobTag("kind", "secondary")]);

        var arguments = await provider.GetRequiredService<IJobPayloadSerializer>()
          .DeserializeAsync(secondRequest.SerializedArguments, [typeof(string)]);

        arguments.ShouldBe(["beta"]);

        var thirdRequest = store.GetRequest(jobIds[2]);
        thirdRequest.Priority.ShouldBe(3);
        thirdRequest.ServiceType.ShouldBe(typeof(EnqueueTestService).AssemblyQualifiedName);
        thirdRequest.MethodName.ShouldBe(nameof(EnqueueTestService.StaticWithPayloadAsync));
        thirdRequest.InvocationTargetKind.ShouldBe(JobInvocationTargetKind.Static);
    }

    [Fact]
    public async Task EnqueueMany_EmptyBatch_ReturnsEmptyAndDoesNotPersistJob()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero);
        using var provider = CreateProvider(new FakeTimeProvider(timestamp));
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();

        var jobIds = await enqueuer.EnqueueManyAsync([]);

        jobIds.ShouldBeEmpty();
        store.EnqueuedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task EnqueueMany_InvalidItem_ThrowsWithoutPersistingEarlierItems()
    {
        var timestamp = new DateTimeOffset(2026, 4, 19, 10, 30, 0, TimeSpan.Zero);
        using var provider = CreateProvider(new FakeTimeProvider(timestamp));
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("alpha", 42);

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueManyAsync([
            JobEnqueueItem.Create<EnqueueTestService>(
              (service, cancellationToken) => service.HandleAsync(payload, cancellationToken)),
            JobEnqueueItem.Create<EnqueueTestService>(
              (service, cancellationToken) => service.NoTokenAsync()),
          ]).AsTask());

        store.EnqueuedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enqueue_IdempotencyMethodAndArguments_GeneratesStableKeyForSameWork()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("alpha", 42);
        var submission = new JobSubmission(IdempotencyKind: JobIdempotencyKind.MethodAndArguments);

        var first = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          submission);
        var second = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          submission);

        store.GetRequest(first).IdempotencyKey.ShouldNotBeNull();
        store.GetRequest(second).IdempotencyKey.ShouldBe(store.GetRequest(first).IdempotencyKey);
    }

    [Fact]
    public async Task Enqueue_IdempotencyMethodAndArguments_DifferentArgumentsGenerateDifferentKeys()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var submission = new JobSubmission(IdempotencyKind: JobIdempotencyKind.MethodAndArguments);

        var first = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(new SamplePayload("alpha", 42), cancellationToken),
          submission);
        var second = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(new SamplePayload("alpha", 43), cancellationToken),
          submission);

        store.GetRequest(second).IdempotencyKey.ShouldNotBe(store.GetRequest(first).IdempotencyKey);
    }

    [Fact]
    public async Task Enqueue_IdempotencyMethodAndArguments_IgnoresSchedulingMetadata()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var payload = new SamplePayload("alpha", 42);

        var first = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          new JobSubmission(
            Priority: 1,
            ConcurrencyGroupKeys: ["group-a"],
            RetryPolicy: new RetryPolicy(2, RetryBackoffKind.Fixed, TimeSpan.FromSeconds(1)),
            Tags: [new JobTag("tenant", "acme")],
            IdempotencyKind: JobIdempotencyKind.MethodAndArguments));
        var second = await enqueuer.EnqueueAsync<EnqueueTestService>(
          (service, cancellationToken) => service.HandleAsync(payload, cancellationToken),
          new JobSubmission(
            Priority: 10,
            ConcurrencyGroupKeys: ["group-b"],
            RetryPolicy: new RetryPolicy(3, RetryBackoffKind.Exponential, TimeSpan.FromSeconds(2)),
            Tags: [new JobTag("tenant", "contoso")],
            IdempotencyKind: JobIdempotencyKind.MethodAndArguments));

        store.GetRequest(second).IdempotencyKey.ShouldBe(store.GetRequest(first).IdempotencyKey);
    }

    [Fact]
    public async Task Enqueue_IdempotencyWithDelay_ThrowsWithoutPersistingJob()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.HandleStringAsync("alpha", cancellationToken),
            new JobSubmission(
              NotBeforeUtc: DateTimeOffset.UtcNow.AddMinutes(1),
              IdempotencyKind: JobIdempotencyKind.MethodAndArguments)).AsTask());

        store.EnqueuedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enqueue_InvalidIdempotencyKind_ThrowsWithoutPersistingJob()
    {
        using var provider = CreateProvider();
        var enqueuer = provider.GetRequiredService<IJobEnqueuer>();
        var store = provider.GetRequiredService<RecordingJobStore>();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.HandleStringAsync("alpha", cancellationToken),
            new JobSubmission(IdempotencyKind: (JobIdempotencyKind)999)).AsTask());

        store.EnqueuedRequests.ShouldBeEmpty();
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

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync(
            cancellationToken => new EnqueueTestService().HandleStringAsync("new-target", cancellationToken)).AsTask());

        await Should.ThrowAsync<ArgumentException>(
          () => enqueuer.EnqueueAsync<EnqueueTestService>(
            (service, cancellationToken) => service.HandleStringAsync(Job.Resolve<SecondEnqueueTestService>().ToString()!, cancellationToken)).AsTask());
    }

    private static ServiceProvider CreateProvider(TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        services.AddSingleton<RecordingJobStore>();
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<RecordingJobStore>());
        services.AddSheddueller();
        services.AddTransient<EnqueueTestService>();
        services.AddTransient<SecondEnqueueTestService>();

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

        public Task HandleDependencyAsync(ResolvedDependency dependency, CancellationToken cancellationToken)
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

        public static Task StaticWithPayloadAsync(SamplePayload payload, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SecondEnqueueTestService
    {
        public ValueTask HandleValueTaskAsync(string value, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResolvedDependency;

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
