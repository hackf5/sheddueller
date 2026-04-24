namespace Sheddueller.Dashboard.Internal;

using Sheddueller.Storage;

internal sealed class DashboardLiveUpdateStream
{
    public event Func<JobEvent, CancellationToken, ValueTask>? JobEventPublished;

    public async ValueTask NotifyAsync(
        JobEvent jobEvent,
        CancellationToken cancellationToken)
    {
        var handlers = this.JobEventPublished;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<JobEvent, CancellationToken, ValueTask>>())
        {
            await handler(jobEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
