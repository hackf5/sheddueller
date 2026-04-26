namespace Sheddueller.Inspection.Jobs;

using Sheddueller.Storage;

/// <summary>
/// Reconstructed persisted invocation metadata for a job.
/// </summary>
public sealed record JobInvocationInspection(
    JobInvocationTargetKind TargetKind,
    string ServiceType,
    string MethodName,
    IReadOnlyList<JobInvocationParameterInspection> Parameters,
    string SerializedArgumentsContentType,
    long SerializedArgumentsByteCount,
    JobSerializedArgumentsInspectionStatus SerializedArgumentsStatus,
    string? SerializedArgumentsStatusMessage = null);
