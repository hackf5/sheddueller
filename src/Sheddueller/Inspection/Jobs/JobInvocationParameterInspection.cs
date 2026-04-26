namespace Sheddueller.Inspection.Jobs;

using Sheddueller.Storage;

/// <summary>
/// Reconstructed persisted invocation metadata for one job method parameter.
/// </summary>
public sealed record JobInvocationParameterInspection(
    int ParameterIndex,
    string ParameterType,
    JobMethodParameterBinding Binding,
    string? SerializedValueJson = null);
