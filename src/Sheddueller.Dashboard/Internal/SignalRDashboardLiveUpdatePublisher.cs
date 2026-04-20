namespace Sheddueller.Dashboard.Internal;

using Microsoft.AspNetCore.SignalR;

using Sheddueller.Dashboard;

internal sealed class SignalRDashboardLiveUpdatePublisher(
    IHubContext<DashboardUpdatesHub> hubContext,
    DashboardLiveUpdateStream stream) : IDashboardLiveUpdatePublisher
{
    public async ValueTask PublishAsync(
        DashboardJobEvent jobEvent,
        CancellationToken cancellationToken = default)
    {
        await stream.PublishAsync(jobEvent, cancellationToken).ConfigureAwait(false);
        await hubContext.Clients.All.SendAsync("jobEvent", jobEvent, cancellationToken).ConfigureAwait(false);
        await hubContext.Clients.Group(DashboardUpdatesHub.JobGroupName(jobEvent.JobId.ToString("N")))
          .SendAsync("jobEvent", jobEvent, cancellationToken)
          .ConfigureAwait(false);
    }
}
