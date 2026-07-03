namespace Sheddueller.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Storage;

using Shouldly;

public sealed class ConcurrencyGroupManagerTests
{
    [Fact]
    public async Task SetLimit_ValidGroup_PersistsOverrideLimit()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.SetLimitAsync("api", 4);

        store.ConcurrencyLimitRequests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
          request => request.GroupKey.ShouldBe("api"),
          request => request.Limit.ShouldBe(4));
    }

    [Fact]
    public async Task SetDefaultLimit_ValidGroup_PersistsDefaultLimit()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.SetDefaultLimitAsync("api", 3);

        store.ConcurrencyDefaultLimitRequests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
          request => request.GroupKey.ShouldBe("api"),
          request => request.Limit.ShouldBe(3));
        store.ConcurrencyLimitRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClearLimitOverride_ValidGroup_PersistsClearRequest()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.ClearLimitOverrideAsync("api");

        store.ClearConcurrencyLimitOverrideRequests.ShouldHaveSingleItem().GroupKey.ShouldBe("api");
    }

    [Fact]
    public async Task SetLimit_NonPositiveLimit_DoesNotPersist()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await manager.SetLimitAsync("api", 0));

        store.ConcurrencyLimitRequests.ShouldBeEmpty();
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingJobStore>();
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<RecordingJobStore>());
        services.AddSheddueller();

        return services.BuildServiceProvider();
    }
}
