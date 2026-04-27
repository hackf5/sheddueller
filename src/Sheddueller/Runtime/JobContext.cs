namespace Sheddueller.Runtime;

internal sealed class JobContext(
    Guid jobId,
    int attemptNumber,
    CancellationToken cancellationToken) : IJobContext
{
    public Guid JobId { get; } = jobId;

    public int AttemptNumber { get; } = attemptNumber;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}
