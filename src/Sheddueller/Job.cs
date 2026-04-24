namespace Sheddueller;

/// <summary>
/// Expression markers for Sheddueller job invocation.
/// </summary>
public static class Job
{
    /// <summary>
    /// Marker used in enqueue and recurring schedule expressions for runtime job context injection.
    /// </summary>
    public static IJobContext Context
      => throw new InvalidOperationException("Job.Context is a Sheddueller expression marker and cannot be evaluated directly.");

    /// <summary>
    /// Marker used in enqueue and recurring schedule expressions for runtime service resolution.
    /// </summary>
    public static TService Resolve<TService>()
      where TService : notnull
      => throw new InvalidOperationException("Job.Resolve<TService>() is a Sheddueller expression marker and cannot be evaluated directly.");
}
