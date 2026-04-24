namespace Sheddueller.Storage;

/// <summary>
/// Identifies how a persisted job method target is invoked.
/// </summary>
public enum JobInvocationTargetKind
{
    /// <summary>
    /// The handler method is invoked on a service resolved from dependency injection.
    /// </summary>
    Instance = 0,

    /// <summary>
    /// The handler method is static and is invoked without a service instance.
    /// </summary>
    Static = 1,
}
