namespace Sheddueller.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class RegistrationTests
{
    [Fact]
    public void AddSheddueller_InMemoryProvider_RegistersCoreServicesAndProvider()
    {
        var serializer = new PassThroughSerializer();
        var services = new ServiceCollection();

        services.AddSheddueller(builder => builder
          .UseJobPayloadSerializer(serializer)
          .UseInMemoryStore()
          .ConfigureOptions(options => options.NodeId = "node-a"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobEnqueuer>().ShouldNotBeNull();
        provider.GetRequiredService<IConcurrencyGroupManager>().ShouldNotBeNull();
        provider.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
        provider.GetRequiredService<IJobPayloadSerializer>().ShouldBeSameAs(serializer);
        provider.GetServices<IHostedService>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task StartupValidation_MissingJobStoreProvider_FailsStart()
    {
        var services = new ServiceCollection();
        services.AddSheddueller();
        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StartAsync(CancellationToken.None);
            }
        });

        exception.Message.ShouldContain("No Sheddueller job store provider");
    }

    [Fact]
    public async Task ConcurrencyGroupManager_DynamicLimit_PersistsConfiguredLimit()
    {
        var services = new ServiceCollection();
        services.AddSheddueller(builder => builder.UseInMemoryStore());
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.SetLimitAsync("dynamic-group", 3);

        (await manager.GetConfiguredLimitAsync("dynamic-group")).ShouldBe(3);
    }

    [Fact]
    public void HostApplicationBuilder_AddSheddueller_RegistersScheduler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddSheddueller(sheddueller => sheddueller.UseInMemoryStore());
        builder.Services.AddTransient<HostBuilderTestService>();

        using var host = builder.Build();

        host.Services.GetRequiredService<IJobEnqueuer>().ShouldNotBeNull();
        host.Services.GetRequiredService<IJobStore>().ShouldBeOfType<InMemoryJobStore>();
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

    private sealed class HostBuilderTestService;
}
