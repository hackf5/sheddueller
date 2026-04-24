namespace Sheddueller.Runtime;

using Sheddueller.Storage;

internal sealed class NoOpJobEventSink(TimeProvider timeProvider) : IJobEventSink
{
    public ValueTask<JobEvent> AppendAsync(
        AppendJobEventRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new JobEvent(
          Guid.NewGuid(),
          request.JobId,
          0,
          request.Kind,
          timeProvider.GetUtcNow(),
          request.AttemptNumber,
          request.LogLevel,
          request.Message,
          request.ProgressPercent,
          request.Fields));
    }
}

internal sealed class NoOpJobEventNotifier : IJobEventNotifier
{
    public ValueTask NotifyAsync(
        JobEvent jobEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
