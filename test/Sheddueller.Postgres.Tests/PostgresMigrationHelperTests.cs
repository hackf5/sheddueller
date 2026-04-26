namespace Sheddueller.Postgres.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheddueller.Postgres;

using Shouldly;

public sealed class PostgresMigrationHelperTests
{
    [Fact]
    public async Task ApplyShedduellerPostgresMigrationsAsync_ServiceProvider_UsesScopedMigrator()
    {
        var recorder = new MigrationRecorder();
        await using var provider = CreateProvider(recorder);
        using var cancellationTokenSource = new CancellationTokenSource();

        await provider.ApplyShedduellerPostgresMigrationsAsync(cancellationTokenSource.Token);

        recorder.ApplyCount.ShouldBe(1);
        recorder.CancellationToken.ShouldBe(cancellationTokenSource.Token);
        recorder.ScopeWasDisposedDuringApply.ShouldBeFalse();
        recorder.ScopeWasDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyShedduellerPostgresMigrationsAsync_Host_UsesScopedMigrator()
    {
        var builder = Host.CreateApplicationBuilder();
        var recorder = new MigrationRecorder();
        RegisterMigrator(builder.Services, recorder);
        using var host = builder.Build();
        using var cancellationTokenSource = new CancellationTokenSource();

        await host.ApplyShedduellerPostgresMigrationsAsync(cancellationTokenSource.Token);

        recorder.ApplyCount.ShouldBe(1);
        recorder.CancellationToken.ShouldBe(cancellationTokenSource.Token);
        recorder.ScopeWasDisposedDuringApply.ShouldBeFalse();
        recorder.ScopeWasDisposed.ShouldBeTrue();
    }

    private static ServiceProvider CreateProvider(MigrationRecorder recorder)
    {
        var services = new ServiceCollection();
        RegisterMigrator(services, recorder);
        return services.BuildServiceProvider();
    }

    private static void RegisterMigrator(
        IServiceCollection services,
        MigrationRecorder recorder)
    {
        services.AddSingleton(recorder);
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IPostgresMigrator, RecordingMigrator>();
    }

    private sealed class MigrationRecorder
    {
        public int ApplyCount { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public bool ScopeWasDisposed { get; set; }

        public bool ScopeWasDisposedDuringApply { get; set; }
    }

    private sealed class ScopeMarker(MigrationRecorder recorder) : IDisposable
    {
        public void Dispose()
          => recorder.ScopeWasDisposed = true;
    }

    private sealed class RecordingMigrator(
        MigrationRecorder recorder,
        ScopeMarker scopeMarker) : IPostgresMigrator
    {
        public ValueTask ApplyAsync(CancellationToken cancellationToken = default)
        {
            _ = scopeMarker;
            recorder.ApplyCount++;
            recorder.CancellationToken = cancellationToken;
            recorder.ScopeWasDisposedDuringApply = recorder.ScopeWasDisposed;
            return ValueTask.CompletedTask;
        }
    }
}
