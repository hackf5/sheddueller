namespace Sheddueller.Runtime;

using Sheddueller.Storage;

internal sealed class JobManager(
    IJobStore store,
    TimeProvider timeProvider) : IJobManager
{
    public ValueTask<JobCancellationResult> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
      => store.CancelAsync(new CancelJobRequest(jobId, timeProvider.GetUtcNow()), cancellationToken);
}
