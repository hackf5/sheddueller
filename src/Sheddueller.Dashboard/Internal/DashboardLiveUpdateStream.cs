namespace Sheddueller.Dashboard.Internal;

using Sheddueller.Dashboard;

internal sealed class DashboardLiveUpdateStream
{
    public event Func<DashboardJobEvent, CancellationToken, ValueTask>? JobEventPublished;

    public async ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken)
    {
        var handlers = this.JobEventPublished;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<DashboardJobEvent, CancellationToken, ValueTask>>())
        {
            await handler(jobEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
