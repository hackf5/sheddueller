namespace Sheddueller.Inspection.Nodes;

/// <summary>
/// Worker node inspection health state.
/// </summary>
public enum NodeHealthState
{
    /// <summary>
    /// The worker is heartbeating within the active threshold.
    /// </summary>
    Active,

    /// <summary>
    /// The worker has missed the active threshold but not the dead threshold.
    /// </summary>
    Stale,

    /// <summary>
    /// The worker has not heartbeated within the dead threshold.
    /// </summary>
    Dead,
}
