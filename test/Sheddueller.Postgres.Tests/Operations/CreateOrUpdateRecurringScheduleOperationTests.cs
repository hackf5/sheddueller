namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Storage;

using Shouldly;

public sealed class CreateOrUpdateRecurringScheduleOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task CreateOrUpdateRecurringSchedule_Create_PersistsDefinitionAndGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        var result = await context.Store.CreateOrUpdateRecurringScheduleAsync(PostgresTestData.CreateSchedule(
          "schedule-a",
          priority: 5,
          groupKeys: ["beta", "alpha", "alpha"],
          retryPolicy: new RetryPolicy(3, RetryBackoffKind.Exponential, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)),
          overlapMode: RecurringOverlapMode.Allow));

        result.ShouldBe(RecurringScheduleUpsertResult.Created);
        var schedule = await context.ReadScheduleAsync("schedule-a");
        schedule.CronExpression.ShouldBe("* * * * *");
        schedule.IsPaused.ShouldBeFalse();
        schedule.OverlapMode.ShouldBe("Allow");
        schedule.Priority.ShouldBe(5);
        schedule.ServiceType.ShouldBe(PostgresTestData.ServiceType);
        schedule.MethodName.ShouldBe(PostgresTestData.MethodName);
        schedule.RetryPolicyConfigured.ShouldBeTrue();
        schedule.MaxAttempts.ShouldBe(3);
        schedule.RetryBackoffKind.ShouldBe("Exponential");
        schedule.RetryBaseDelayMs.ShouldBe(2000);
        schedule.RetryMaxDelayMs.ShouldBe(8000);
        schedule.NextFireAtUtc.ShouldNotBeNull();
        (await context.ReadScheduleGroupKeysAsync("schedule-a")).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task CreateOrUpdateRecurringSchedule_Unchanged_ReturnsUnchanged()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var request = PostgresTestData.CreateSchedule("schedule-a");
        await context.Store.CreateOrUpdateRecurringScheduleAsync(request);

        (await context.Store.CreateOrUpdateRecurringScheduleAsync(request)).ShouldBe(RecurringScheduleUpsertResult.Unchanged);
    }

    [Fact]
    public async Task CreateOrUpdateRecurringSchedule_UpdateActiveSchedule_ReplacesDefinitionAndGroups()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await context.Store.CreateOrUpdateRecurringScheduleAsync(PostgresTestData.CreateSchedule("schedule-a", priority: 1, groupKeys: ["old"]));

        var result = await context.Store.CreateOrUpdateRecurringScheduleAsync(PostgresTestData.CreateSchedule("schedule-a", priority: 9, groupKeys: ["new"]));

        result.ShouldBe(RecurringScheduleUpsertResult.Updated);
        (await context.ReadScheduleAsync("schedule-a")).Priority.ShouldBe(9);
        (await context.ReadScheduleGroupKeysAsync("schedule-a")).ShouldBe(["new"]);
    }

    [Fact]
    public async Task CreateOrUpdateRecurringSchedule_UpdatePausedSchedule_PreservesPausedStateAndNullNextFire()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await context.Store.CreateOrUpdateRecurringScheduleAsync(PostgresTestData.CreateSchedule("schedule-a", priority: 1));
        await context.Store.PauseRecurringScheduleAsync("schedule-a", DateTimeOffset.UtcNow);

        var result = await context.Store.CreateOrUpdateRecurringScheduleAsync(PostgresTestData.CreateSchedule("schedule-a", priority: 2));

        result.ShouldBe(RecurringScheduleUpsertResult.Updated);
        var schedule = await context.ReadScheduleAsync("schedule-a");
        schedule.IsPaused.ShouldBeTrue();
        schedule.NextFireAtUtc.ShouldBeNull();
        schedule.Priority.ShouldBe(2);
    }
}
