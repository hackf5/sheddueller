namespace Sheddueller.Postgres.Tests;

using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Sheddueller.Postgres;
using Sheddueller.Runtime;
using Sheddueller.Serialization;
using Sheddueller.Storage;

using Shouldly;

public sealed class PostgresProviderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset AppClock = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task Migration_FreshSchema_CreatesSchemaAndStampsVersion()
    {
        await using var provider = CreateProvider(CreateSchemaName());
        var migrator = provider.GetRequiredService<IPostgresMigrator>();

        await migrator.ApplyAsync();

        (await this.ReadSchemaVersionAsync(provider.GetRequiredService<ShedduellerPostgresOptions>().SchemaName)).ShouldBe(1);
    }

    [Fact]
    public async Task StartupValidation_MissingSchema_FailsBeforeWorkerStarts()
    {
        var schemaName = CreateSchemaName();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSheddueller(sheddueller => sheddueller.UsePostgres(options =>
        {
            options.DataSource = fixture.DataSource;
            options.SchemaName = schemaName;
        }));
        using var host = builder.Build();

        await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task TryClaim_ConcurrentNodes_ClaimsTaskOnlyOnce()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var store = provider.GetRequiredService<ITaskStore>();
        await store.EnqueueAsync(CreateRequest(Guid.NewGuid()));

        var results = await Task.WhenAll(Enumerable.Range(0, 2)
          .Select(index =>
          {
              var now = DateTimeOffset.UtcNow;
              return store.TryClaimNextAsync(new ClaimTaskRequest($"node-{index}", now, now.AddSeconds(30))).AsTask();
          }));

        results.Count(result => result is ClaimTaskResult.Claimed).ShouldBe(1);
        results.Count(result => result is ClaimTaskResult.NoTaskAvailable).ShouldBe(1);
    }

    [Fact]
    public async Task TryClaim_PriorityAndFifo_ClaimsHigherPriorityThenOldest()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var store = provider.GetRequiredService<ITaskStore>();
        var firstLow = Guid.NewGuid();
        var secondLow = Guid.NewGuid();
        var high = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(firstLow, priority: 0));
        await store.EnqueueAsync(CreateRequest(secondLow, priority: 0));
        await store.EnqueueAsync(CreateRequest(high, priority: 10));

        (await ClaimAsync(store)).TaskId.ShouldBe(high);
        (await ClaimAsync(store)).TaskId.ShouldBe(firstLow);
        (await ClaimAsync(store)).TaskId.ShouldBe(secondLow);
    }

    [Fact]
    public async Task TryClaim_SaturatedGroup_ClaimsNextEligibleTask()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var store = provider.GetRequiredService<ITaskStore>();
        var running = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var eligible = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(running, priority: 100, groupKeys: ["shared"]));
        (await ClaimAsync(store)).TaskId.ShouldBe(running);

        await store.EnqueueAsync(CreateRequest(blocked, priority: 100, groupKeys: ["shared"]));
        await store.EnqueueAsync(CreateRequest(eligible, priority: 0));

        (await ClaimAsync(store)).TaskId.ShouldBe(eligible);
    }

    [Fact]
    public async Task LeaseRecovery_ExpiredClaim_RequeuesThenFailsByRetryPolicy()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var store = provider.GetRequiredService<ITaskStore>();
        var taskId = Guid.NewGuid();
        await store.EnqueueAsync(CreateRequest(
          taskId,
          maxAttempts: 2,
          retryBackoffKind: RetryBackoffKind.Fixed,
          retryBaseDelay: TimeSpan.FromMilliseconds(1)));

        var firstClaim = await ClaimAsync(store, leaseDuration: TimeSpan.FromMilliseconds(1));
        firstClaim.AttemptCount.ShouldBe(1);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        (await store.RecoverExpiredLeasesAsync(new RecoverExpiredLeasesRequest(AppClock))).ShouldBe(1);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        var secondClaim = await ClaimAsync(store);
        secondClaim.AttemptCount.ShouldBe(2);

        (await store.MarkFailedAsync(new FailTaskRequest(taskId, "node-1", secondClaim.LeaseToken, AppClock, CreateFailure()))).ShouldBeTrue();
        (await this.ReadTaskStateAsync(provider.GetRequiredService<ShedduellerPostgresOptions>().SchemaName, taskId)).ShouldBe("Failed");
    }

    [Fact]
    public async Task DatabaseTime_AppClockSkew_UsesDatabaseTimestampForEnqueue()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var store = provider.GetRequiredService<ITaskStore>();
        var taskId = Guid.NewGuid();

        await store.EnqueueAsync(CreateRequest(taskId, enqueuedAtUtc: new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var enqueuedAtUtc = await this.ReadTaskEnqueuedAtAsync(provider.GetRequiredService<ShedduellerPostgresOptions>().SchemaName, taskId);
        enqueuedAtUtc.Year.ShouldNotBe(1999);
        enqueuedAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task RecurringSchedule_ConcurrentMaterialization_CreatesOneOccurrence()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var schemaName = provider.GetRequiredService<ShedduellerPostgresOptions>().SchemaName;
        var store = provider.GetRequiredService<ITaskStore>();
        await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a"));
        await this.MakeScheduleDueAsync(schemaName, "schedule-a");

        var results = await Task.WhenAll(Enumerable.Range(0, 2)
          .Select(_ => store.MaterializeDueRecurringSchedulesAsync(new MaterializeDueRecurringSchedulesRequest(AppClock, null)).AsTask()));

        results.Sum().ShouldBe(1);
        var claimed = await ClaimAsync(store);
        claimed.SourceScheduleKey.ShouldBe("schedule-a");
    }

    [Fact]
    public async Task RecurringSchedule_UpsertWhilePaused_PreservesPauseStateAndRecomputesOnResume()
    {
        await using var provider = await CreateMigratedProviderAsync();
        var store = provider.GetRequiredService<ITaskStore>();

        (await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 0))).ShouldBe(RecurringScheduleUpsertResult.Created);
        (await store.PauseRecurringScheduleAsync("schedule-a", AppClock)).ShouldBeTrue();
        (await store.CreateOrUpdateRecurringScheduleAsync(CreateSchedule("schedule-a", priority: 1))).ShouldBe(RecurringScheduleUpsertResult.Updated);

        var paused = await store.GetRecurringScheduleAsync("schedule-a");
        paused.ShouldNotBeNull().IsPaused.ShouldBeTrue();
        paused.NextFireAtUtc.ShouldBeNull();

        (await store.ResumeRecurringScheduleAsync("schedule-a", AppClock)).ShouldBeTrue();
        var resumed = await store.GetRecurringScheduleAsync("schedule-a");
        resumed.ShouldNotBeNull().IsPaused.ShouldBeFalse();
        resumed.NextFireAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task WakeSignal_PostgresNotify_WakesWaitingProvider()
    {
        var schemaName = CreateSchemaName();
        await using var firstProvider = CreateProvider(schemaName);
        await firstProvider.GetRequiredService<IPostgresMigrator>().ApplyAsync();
        await using var secondProvider = CreateProvider(schemaName);
        var waitTask = firstProvider.GetRequiredService<IShedduellerWakeSignal>()
          .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
          .AsTask();

        await secondProvider.GetRequiredService<ITaskStore>().EnqueueAsync(CreateRequest(Guid.NewGuid()));

        await waitTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async ValueTask<ServiceProvider> CreateMigratedProviderAsync()
    {
        var provider = CreateProvider(CreateSchemaName());
        await provider.GetRequiredService<IPostgresMigrator>().ApplyAsync();
        return provider;
    }

    private ServiceProvider CreateProvider(string schemaName)
    {
        var services = new ServiceCollection();
        services.AddSheddueller(sheddueller => sheddueller.UsePostgres(options =>
        {
            options.DataSource = fixture.DataSource;
            options.SchemaName = schemaName;
        }));

        return services.BuildServiceProvider();
    }

    private async ValueTask<int?> ReadSchemaVersionAsync(string schemaName)
    {
        await using var command = fixture.DataSource.CreateCommand(
          $"select schema_version from {Table(schemaName, "schema_info")} where singleton_id = 1;");
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async ValueTask<string> ReadTaskStateAsync(string schemaName, Guid taskId)
    {
        await using var command = fixture.DataSource.CreateCommand(
          $"select state from {Table(schemaName, "tasks")} where task_id = @task_id;");
        command.Parameters.AddWithValue("task_id", taskId);
        return (string)(await command.ExecuteScalarAsync()
          ?? throw new InvalidOperationException($"Task '{taskId}' was not found."));
    }

    private async ValueTask<DateTimeOffset> ReadTaskEnqueuedAtAsync(string schemaName, Guid taskId)
    {
        await using var command = fixture.DataSource.CreateCommand(
          $"select enqueued_at_utc from {Table(schemaName, "tasks")} where task_id = @task_id;");
        command.Parameters.AddWithValue("task_id", taskId);
        var value = await command.ExecuteScalarAsync()
          ?? throw new InvalidOperationException($"Task '{taskId}' was not found.");

        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Unexpected PostgreSQL timestamp value."),
        };
    }

    private async ValueTask MakeScheduleDueAsync(string schemaName, string scheduleKey)
    {
        await using var command = fixture.DataSource.CreateCommand(
          $"""
          update {Table(schemaName, "recurring_schedules")}
          set next_fire_at_utc = transaction_timestamp() - interval '1 minute'
          where schedule_key = @schedule_key;
          """);
        command.Parameters.AddWithValue("schedule_key", scheduleKey);
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask<ClaimedTask> ClaimAsync(
        ITaskStore store,
        string nodeId = "node-1",
        TimeSpan? leaseDuration = null)
    {
        var now = DateTimeOffset.UtcNow;
        return (await store.TryClaimNextAsync(new ClaimTaskRequest(nodeId, now, now.Add(leaseDuration ?? TimeSpan.FromSeconds(30)))))
          .ShouldBeOfType<ClaimTaskResult.Claimed>()
          .Task;
    }

    private static EnqueueTaskRequest CreateRequest(
        Guid taskId,
        int priority = 0,
        DateTimeOffset? enqueuedAtUtc = null,
        int maxAttempts = 1,
        RetryBackoffKind? retryBackoffKind = null,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null,
        IReadOnlyList<string>? groupKeys = null)
      => new(
        taskId,
        priority,
        typeof(PostgresTestService).AssemblyQualifiedName!,
        nameof(PostgresTestService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        groupKeys ?? [],
        enqueuedAtUtc ?? AppClock,
        null,
        maxAttempts,
        retryBackoffKind,
        retryBaseDelay,
        retryMaxDelay);

    private static UpsertRecurringScheduleRequest CreateSchedule(string scheduleKey, int priority = 0)
      => new(
        scheduleKey,
        "* * * * *",
        typeof(PostgresTestService).AssemblyQualifiedName!,
        nameof(PostgresTestService.RunAsync),
        [typeof(CancellationToken).AssemblyQualifiedName!],
        EmptyPayload(),
        priority,
        [],
        null,
        RecurringOverlapMode.Skip,
        AppClock);

    private static TaskFailureInfo CreateFailure()
      => new("TestException", "failed", null);

    private static SerializedTaskPayload EmptyPayload()
      => new(SystemTextJsonTaskPayloadSerializer.JsonContentType, "[]"u8.ToArray());

    private static string CreateSchemaName()
      => "sheddueller_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static string Table(string schemaName, string tableName)
      => $"{Quote(schemaName)}.{Quote(tableName)}";

    private static string Quote(string identifier)
      => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed class PostgresTestService
    {
        public Task RunAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
}
