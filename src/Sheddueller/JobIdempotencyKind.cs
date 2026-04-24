namespace Sheddueller;

/// <summary>
/// Identifies how Sheddueller derives an idempotency key for a submitted job.
/// </summary>
public enum JobIdempotencyKind
{
    /// <summary>
    /// No idempotency key is generated.
    /// </summary>
    None = 0,

    /// <summary>
    /// Generate a key from the submitted handler identity and serialized method arguments.
    /// </summary>
    MethodAndArguments = 1,
}
