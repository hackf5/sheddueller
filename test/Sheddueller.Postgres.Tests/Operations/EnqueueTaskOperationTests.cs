namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Serialization;

using Shouldly;

public sealed class EnqueueTaskOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Enqueue_PersistsTaskFieldsAndGroupRows()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var taskId = Guid.NewGuid();
        var notBeforeUtc = DateTimeOffset.UtcNow.AddMinutes(5);

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(
          taskId,
          priority: 7,
          enqueuedAtUtc: new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero),
          notBeforeUtc: notBeforeUtc,
          maxAttempts: 3,
          retryBackoffKind: RetryBackoffKind.Exponential,
          retryBaseDelay: TimeSpan.FromSeconds(2),
          retryMaxDelay: TimeSpan.FromSeconds(9),
          groupKeys: ["beta", "alpha", "alpha"]));

        var task = await context.ReadTaskAsync(taskId);

        task.State.ShouldBe("Queued");
        task.Priority.ShouldBe(7);
        task.EnqueuedAtUtc.Year.ShouldNotBe(1999);
        task.NotBeforeUtc.ShouldNotBeNull().ShouldBe(notBeforeUtc, TimeSpan.FromMilliseconds(1));
        task.ServiceType.ShouldBe(PostgresTestData.ServiceType);
        task.MethodName.ShouldBe(PostgresTestData.MethodName);
        task.MethodParameterTypes.ShouldBe([typeof(CancellationToken).AssemblyQualifiedName!]);
        task.SerializedArgumentsContentType.ShouldBe(SystemTextJsonTaskPayloadSerializer.JsonContentType);
        task.SerializedArguments.ShouldBe("[]"u8.ToArray());
        task.AttemptCount.ShouldBe(0);
        task.MaxAttempts.ShouldBe(3);
        task.RetryBackoffKind.ShouldBe("Exponential");
        task.RetryBaseDelayMs.ShouldBe(2000);
        task.RetryMaxDelayMs.ShouldBe(9000);
        (await context.ReadTaskGroupKeysAsync(taskId)).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task Enqueue_AssignsIncreasingSequences()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var firstResult = await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(first));
        var secondResult = await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(second));

        secondResult.EnqueueSequence.ShouldBeGreaterThan(firstResult.EnqueueSequence);
        (await context.ReadTaskAsync(first)).EnqueueSequence.ShouldBe(firstResult.EnqueueSequence);
        (await context.ReadTaskAsync(second)).EnqueueSequence.ShouldBe(secondResult.EnqueueSequence);
    }
}
