namespace Sheddueller;

/// <summary>
/// Defines a smooth concurrency-group job-start rate.
/// </summary>
/// <param name="PermitCount">The number of job starts permitted during <paramref name="Period"/>.</param>
/// <param name="Period">The period over which <paramref name="PermitCount"/> starts are evenly spaced.</param>
public sealed record ConcurrencyGroupRateLimit(
    int PermitCount,
    TimeSpan Period);
