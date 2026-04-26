namespace Sheddueller.Tests;

using Microsoft.Extensions.Logging;

using Sheddueller.Runtime;
using Sheddueller.Storage;
using Sheddueller.Tests.Logging;

using Shouldly;

public sealed class JobContextLoggingTests
{
    [Fact]
    public async Task Log_EventSinkFails_LogsDiagnosticAndDoesNotFailJob()
    {
        var jobId = Guid.NewGuid();
        using var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
          .SetMinimumLevel(LogLevel.Trace)
          .AddProvider(logs));
        var context = new JobContext(
          jobId,
          1,
          new FailingJobEventSink(),
          loggerFactory.CreateLogger<JobContext>(),
          CancellationToken.None);

        await context.LogAsync(JobLogLevel.Information, "hello");

        var entry = logs.SingleByEventId(1040);
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeOfType<InvalidOperationException>();
        entry.Properties["JobId"].ShouldBe(jobId);
        entry.MessageTemplate.ShouldBe("Failed to append durable job event for job {JobId}.");
    }

    private sealed class FailingJobEventSink : IJobEventSink
    {
        public ValueTask<JobEvent> AppendAsync(
            AppendJobEventRequest request,
            CancellationToken cancellationToken = default)
          => throw new InvalidOperationException("append failed");
    }
}
