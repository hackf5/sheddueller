namespace Sheddueller.Worker.Internal;

internal sealed record CapturedJobLogContext(
    Guid ExecutionId,
    Guid JobId,
    int AttemptNumber);
