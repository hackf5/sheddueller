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
}
