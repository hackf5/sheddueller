namespace Sheddueller;

/// <summary>
/// Submission options for an enqueued job.
/// </summary>
/// <param name="Priority">The queue priority. Higher values are claimed before lower values.</param>
/// <param name="ConcurrencyGroupKeys">Optional dynamic concurrency groups the job must acquire before execution.</param>
/// <param name="NotBeforeUtc">The earliest UTC instant at which the job may be claimed.</param>
/// <param name="RetryPolicy">The retry policy for this job. Null uses <see cref="ShedduellerOptions.DefaultRetryPolicy"/>.</param>
/// <param name="Tags">Optional searchable metadata copied to the stored job.</param>
/// <param name="IdempotencyKind">How Sheddueller derives an idempotency key for this submission.</param>
public sealed record JobSubmission(
    int Priority = 0,
    IReadOnlyList<string>? ConcurrencyGroupKeys = null,
    DateTimeOffset? NotBeforeUtc = null,
    RetryPolicy? RetryPolicy = null,
    IReadOnlyList<JobTag>? Tags = null,
    JobIdempotencyKind IdempotencyKind = JobIdempotencyKind.None);
