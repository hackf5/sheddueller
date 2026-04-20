namespace Sheddueller.Dashboard.Internal;

using Microsoft.AspNetCore.SignalR;

internal sealed class DashboardUpdatesHub : Hub
{
    public Task WatchJob(string taskId)
      => this.Groups.AddToGroupAsync(this.Context.ConnectionId, JobGroupName(taskId));

    public Task UnwatchJob(string taskId)
      => this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, JobGroupName(taskId));

    public static string JobGroupName(string taskId)
      => $"job:{taskId}";
}
