namespace Sheddueller;

/// <summary>
/// Submission options for an enqueued job.
/// </summary>
public sealed record JobSubmission(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    DateTimeOffset? NotBeforeUtc = null,
    RetryPolicy? RetryPolicy = null,
    IReadOnlyList<JobTag>? Tags = null);
