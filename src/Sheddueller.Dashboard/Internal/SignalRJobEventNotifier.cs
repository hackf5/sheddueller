namespace Sheddueller.Dashboard.Internal;

using Microsoft.AspNetCore.SignalR;

using Sheddueller.Storage;

internal sealed class SignalRJobEventNotifier(
    IHubContext<DashboardUpdatesHub> hubContext,
    DashboardLiveUpdateStream stream) : IJobEventNotifier
{
    public async ValueTask NotifyAsync(
        JobEvent jobEvent,
        CancellationToken cancellationToken = default)
    {
        await stream.NotifyAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        await hubContext.Clients.All.SendAsync("jobEvent", jobEvent, cancellationToken).ConfigureAwait(false);
        await hubContext.Clients.Group(DashboardUpdatesHub.JobGroupName(jobEvent.JobId.ToString("N")))
          .SendAsync("jobEvent", jobEvent, cancellationToken)
          .ConfigureAwait(false);
    }
}
