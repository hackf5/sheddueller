namespace Sheddueller.Dashboard.Internal;

using Microsoft.AspNetCore.SignalR;

internal sealed class DashboardUpdatesHub : Hub
{
    public Task WatchJob(string jobId)
      => this.Groups.AddToGroupAsync(this.Context.ConnectionId, JobGroupName(jobId));

    public Task UnwatchJob(string jobId)
      => this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, JobGroupName(jobId));

    public static string JobGroupName(string jobId)
      => $"job:{jobId}";
}
