namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Describes whether persisted serialized job arguments can be displayed for inspection.
/// </summary>
public enum JobSerializedArgumentsInspectionStatus
{
    /// <summary>
    /// The serialized arguments were parsed and mapped to invocation parameters.
    /// </summary>
    Displayable = 0,

    /// <summary>
    /// The payload content type is not understood by the built-in inspection renderer.
    /// </summary>
    UnsupportedContentType = 1,

    /// <summary>
    /// The payload exceeds the inspection display limit.
    /// </summary>
    TooLarge = 2,

    /// <summary>
    /// The payload was expected to be displayable but could not be parsed.
    /// </summary>
    InvalidPayload = 3,

    /// <summary>
    /// The payload argument count does not match the reconstructed invocation.
    /// </summary>
    ArgumentCountMismatch = 4,
}
