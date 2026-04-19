namespace Sheddueller.Runtime;

using Sheddueller.Storage;

internal sealed class TaskManager(
    ITaskStore store,
    TimeProvider timeProvider) : ITaskManager
{
    public ValueTask<bool> CancelAsync(Guid taskId, CancellationToken cancellationToken = default)
      => store.CancelAsync(new CancelTaskRequest(taskId, timeProvider.GetUtcNow()), cancellationToken);
}
