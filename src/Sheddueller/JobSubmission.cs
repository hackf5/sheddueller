namespace Sheddueller;

/// <summary>
/// Submission options for an enqueued task.
/// </summary>
public sealed record TaskSubmission(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    DateTimeOffset? NotBeforeUtc = null,
    RetryPolicy? RetryPolicy = null,
    IReadOnlyList<JobTag>? Tags = null);
