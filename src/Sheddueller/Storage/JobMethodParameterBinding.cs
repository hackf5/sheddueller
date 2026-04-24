namespace Sheddueller.Storage;

/// <summary>
/// Identifies how a persisted job method parameter is supplied at execution time.
/// </summary>
public sealed record JobMethodParameterBinding(
    JobMethodParameterBindingKind Kind,
    string? ServiceType = null);
