namespace Sheddueller.Dashboard.Internal;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using Sheddueller.Storage;

internal sealed class SignalRJobEventNotifier(
    IHubContext<DashboardUpdatesHub> hubContext,
    DashboardLiveUpdateStream stream,
    ILogger<SignalRJobEventNotifier> logger) : IJobEventNotifier
{
    public async ValueTask NotifyAsync(
        JobEvent jobEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await stream.NotifyAsync(jobEvent, cancellationToken).ConfigureAwait(false);
            await hubContext.Clients.All.SendAsync("jobEvent", jobEvent, cancellationToken).ConfigureAwait(false);
            await hubContext.Clients.Group(DashboardUpdatesHub.JobGroupName(jobEvent.JobId.ToString("N")))
              .SendAsync("jobEvent", jobEvent, cancellationToken)
              .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.DashboardJobEventPublishFailed(exception, jobEvent.JobId, jobEvent.EventSequence);
            throw;
        }
    }
}
