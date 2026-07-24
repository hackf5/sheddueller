namespace Sheddueller;

/// <summary>
/// Describes the live rate-limit override configured for a concurrency group.
/// </summary>
public enum ConcurrencyGroupRateLimitOverrideKind
{
    /// <summary>
    /// The group inherits its code-defined default rate.
    /// </summary>
    Inherit,

    /// <summary>
    /// The group has a limited live rate override.
    /// </summary>
    Limited,

    /// <summary>
    /// The group has an explicitly unlimited live rate override.
    /// </summary>
    Unlimited,
}
