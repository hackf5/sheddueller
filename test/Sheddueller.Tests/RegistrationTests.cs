namespace Sheddueller.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Sheddueller.Runtime;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class RegistrationTests
{
    [Fact]
    public void AddSheddueller_CustomProvider_RegistersCoreServices()
    {
        var serializer = new PassThroughSerializer();
        var services = new ServiceCollection();
        services.AddSingleton<RecordingJobStore>();
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<RecordingJobStore>());

        services.AddSheddueller(builder => builder
          .UseJobPayloadSerializer(serializer)
          .ConfigureOptions(options => options.NodeId = "node-a"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobEnqueuer>().ShouldNotBeNull();
        provider.GetRequiredService<IConcurrencyGroupManager>().ShouldNotBeNull();
        provider.GetRequiredService<IJobStore>().ShouldBeSameAs(provider.GetRequiredService<RecordingJobStore>());
        provider.GetRequiredService<IJobPayloadSerializer>().ShouldBeSameAs(serializer);
        provider.GetServices<IHostedService>().ShouldBeEmpty();
        provider.GetServices<IShedduellerStartupValidator>().Count().ShouldBe(1);
    }

    [Fact]
    public void AddSheddueller_MissingJobStoreProvider_DoesNotRegisterHostedValidation()
    {
        var services = new ServiceCollection();
        services.AddSheddueller();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().ShouldBeEmpty();
    }

    [Fact]
    public void AddSheddueller_WorkerOnlyOptions_AreNotHostedValidated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingJobStore>();
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<RecordingJobStore>());
        services.AddSheddueller(builder => builder.ConfigureOptions(options => options.MaxConcurrentExecutionsPerNode = 0));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().ShouldBeEmpty();
    }

    [Fact]
    public void AddSheddueller_ServiceProviderAwareOptions_CanReadRegisteredServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new NodeConfiguration("node-from-di"));
        services.AddSheddueller(builder => builder.ConfigureOptions((serviceProvider, options) =>
        {
            options.NodeId = serviceProvider.GetRequiredService<NodeConfiguration>().NodeId;
        }));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ShedduellerOptions>>().Value.NodeId.ShouldBe("node-from-di");
    }

    private sealed class PassThroughSerializer : IJobPayloadSerializer
    {
        public ValueTask<SerializedJobPayload> SerializeAsync(
          IReadOnlyList<object?> arguments,
          IReadOnlyList<Type> parameterTypes,
          CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new SerializedJobPayload("test/pass-through", []));
        }

        public ValueTask<IReadOnlyList<object?>> DeserializeAsync(
          SerializedJobPayload payload,
          IReadOnlyList<Type> parameterTypes,
          CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<object?>>(Array.Empty<object?>());
        }
    }

    private sealed record NodeConfiguration(string NodeId);
}
