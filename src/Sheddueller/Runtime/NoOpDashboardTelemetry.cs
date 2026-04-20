namespace Sheddueller.Runtime;

using Sheddueller.Dashboard;

internal sealed class NoOpDashboardEventSink(TimeProvider timeProvider) : IDashboardEventSink
{
    public ValueTask<DashboardJobEvent> AppendAsync(
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DashboardJobEvent(
          Guid.NewGuid(),
          request.TaskId,
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

internal sealed class NoOpDashboardLiveUpdatePublisher : IDashboardLiveUpdatePublisher
{
    public ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
