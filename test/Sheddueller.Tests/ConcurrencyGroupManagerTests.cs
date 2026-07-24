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

    [Fact]
    public async Task SetRateLimit_ValidRate_PersistsOverride()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();
        var rateLimit = new ConcurrencyGroupRateLimit(2, TimeSpan.FromSeconds(1));

        await manager.SetRateLimitAsync("api", rateLimit);

        store.ConcurrencyRateLimitRequests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
          request => request.GroupKey.ShouldBe("api"),
          request => request.RateLimit.ShouldBe(rateLimit));
    }

    [Fact]
    public async Task SetDefaultRateLimit_ValidRate_PersistsDefault()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();
        var rateLimit = new ConcurrencyGroupRateLimit(10, TimeSpan.FromMinutes(1));

        await manager.SetDefaultRateLimitAsync("api", rateLimit);

        store.ConcurrencyDefaultRateLimitRequests.ShouldHaveSingleItem().RateLimit.ShouldBe(rateLimit);
        store.ConcurrencyRateLimitRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClearDefaultRateLimit_ValidGroup_PersistsClear()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.ClearDefaultRateLimitAsync("api");

        store.ClearConcurrencyDefaultRateLimitRequests.ShouldHaveSingleItem().GroupKey.ShouldBe("api");
    }

    [Fact]
    public async Task SetUnlimitedRateLimit_ValidGroup_PersistsUnlimitedOverride()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.SetUnlimitedRateLimitAsync("api");

        store.ConcurrencyUnlimitedRateLimitRequests.ShouldHaveSingleItem().GroupKey.ShouldBe("api");
    }

    [Fact]
    public async Task ClearRateLimitOverride_ValidGroup_PersistsClear()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();

        await manager.ClearRateLimitOverrideAsync("api");

        store.ClearConcurrencyRateLimitOverrideRequests.ShouldHaveSingleItem().GroupKey.ShouldBe("api");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public async Task SetRateLimit_InvalidRate_DoesNotPersist(
        int permitCount,
        long periodTicks)
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<RecordingJobStore>();
        var manager = provider.GetRequiredService<IConcurrencyGroupManager>();
        var rateLimit = new ConcurrencyGroupRateLimit(permitCount, TimeSpan.FromTicks(periodTicks));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await manager.SetRateLimitAsync("api", rateLimit));

        store.ConcurrencyRateLimitRequests.ShouldBeEmpty();
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
