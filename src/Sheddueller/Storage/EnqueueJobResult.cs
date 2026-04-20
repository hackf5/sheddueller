namespace Sheddueller.Storage;

/// <summary>
/// Store result for an enqueued job.
/// </summary>
public sealed record EnqueueJobResult(
    Guid JobId,
    long EnqueueSequence);
