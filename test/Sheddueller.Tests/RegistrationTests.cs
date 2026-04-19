using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Sheddueller.Tests;

public sealed class RegistrationTests
{
  [Fact]
  public void AddShedduellerRegistersCoreServicesAndInMemoryProvider()
  {
    var serializer = new PassThroughSerializer();
    var services = new ServiceCollection();

    services.AddSheddueller(builder => builder
      .UseTaskPayloadSerializer(serializer)
      .UseInMemoryStore()
      .ConfigureOptions(options => options.NodeId = "node-a"));

    using var provider = services.BuildServiceProvider();

    provider.GetRequiredService<ITaskEnqueuer>().ShouldNotBeNull();
    provider.GetRequiredService<IConcurrencyGroupManager>().ShouldNotBeNull();
    provider.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();
    provider.GetRequiredService<ITaskPayloadSerializer>().ShouldBeSameAs(serializer);
    provider.GetServices<IHostedService>().Count().ShouldBe(2);
  }

  [Fact]
  public async Task StartupValidationFailsWhenNoTaskStoreProviderIsRegistered()
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

    exception.Message.ShouldContain("No Sheddueller task store provider");
  }

  [Fact]
  public async Task ConcurrencyGroupManagerPersistsDynamicLimits()
  {
    var services = new ServiceCollection();
    services.AddSheddueller(builder => builder.UseInMemoryStore());
    using var provider = services.BuildServiceProvider();
    var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

    await manager.SetLimitAsync("dynamic-group", 3);

    (await manager.GetConfiguredLimitAsync("dynamic-group")).ShouldBe(3);
  }

  [Fact]
  public void HostApplicationBuilderExtensionRegistersSheddueller()
  {
    var builder = Host.CreateApplicationBuilder();
    builder.AddSheddueller(sheddueller => sheddueller.UseInMemoryStore());
    builder.Services.AddTransient<HostBuilderTestService>();

    using var host = builder.Build();

    host.Services.GetRequiredService<ITaskEnqueuer>().ShouldNotBeNull();
    host.Services.GetRequiredService<ITaskStore>().ShouldBeOfType<InMemoryTaskStore>();
  }

  private sealed class PassThroughSerializer : ITaskPayloadSerializer
  {
    public ValueTask<SerializedTaskPayload> SerializeAsync(
      IReadOnlyList<object?> arguments,
      IReadOnlyList<Type> parameterTypes,
      CancellationToken cancellationToken = default)
    {
      return ValueTask.FromResult(new SerializedTaskPayload("test/pass-through", []));
    }

    public ValueTask<IReadOnlyList<object?>> DeserializeAsync(
      SerializedTaskPayload payload,
      IReadOnlyList<Type> parameterTypes,
      CancellationToken cancellationToken = default)
    {
      return ValueTask.FromResult<IReadOnlyList<object?>>(Array.Empty<object?>());
    }
  }

  private sealed class HostBuilderTestService;
}
