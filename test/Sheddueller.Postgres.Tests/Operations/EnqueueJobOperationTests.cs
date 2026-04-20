namespace Sheddueller.Postgres.Tests.Operations;

using Sheddueller.Serialization;

using Shouldly;

public sealed class EnqueueJobOperationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Enqueue_PersistsJobFieldsAndGroupRows()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var jobId = Guid.NewGuid();
        var notBeforeUtc = DateTimeOffset.UtcNow.AddMinutes(5);

        await context.Store.EnqueueAsync(PostgresTestData.CreateRequest(
          jobId,
          priority: 7,
          enqueuedAtUtc: new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero),
          notBeforeUtc: notBeforeUtc,
          maxAttempts: 3,
          retryBackoffKind: RetryBackoffKind.Exponential,
          retryBaseDelay: TimeSpan.FromSeconds(2),
          retryMaxDelay: TimeSpan.FromSeconds(9),
          groupKeys: ["beta", "alpha", "alpha"]));

        var job = await context.ReadJobAsync(jobId);

        job.State.ShouldBe("Queued");
        job.Priority.ShouldBe(7);
        job.EnqueuedAtUtc.Year.ShouldNotBe(1999);
        job.NotBeforeUtc.ShouldNotBeNull().ShouldBe(notBeforeUtc, TimeSpan.FromMilliseconds(1));
        job.ServiceType.ShouldBe(PostgresTestData.ServiceType);
        job.MethodName.ShouldBe(PostgresTestData.MethodName);
        job.MethodParameterTypes.ShouldBe([typeof(CancellationToken).AssemblyQualifiedName!]);
        job.SerializedArgumentsContentType.ShouldBe(SystemTextJsonJobPayloadSerializer.JsonContentType);
        job.SerializedArguments.ShouldBe("[]"u8.ToArray());
        job.AttemptCount.ShouldBe(0);
        job.MaxAttempts.ShouldBe(3);
        job.RetryBackoffKind.ShouldBe("Exponential");
        job.RetryBaseDelayMs.ShouldBe(2000);
        job.RetryMaxDelayMs.ShouldBe(9000);
        (await context.ReadJobGroupKeysAsync(jobId)).ShouldBe(["alpha", "beta"]);
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
        (await context.ReadJobAsync(first)).EnqueueSequence.ShouldBe(firstResult.EnqueueSequence);
        (await context.ReadJobAsync(second)).EnqueueSequence.ShouldBe(secondResult.EnqueueSequence);
    }
}
