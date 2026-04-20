namespace Sheddueller.Storage;

/// <summary>
/// Best-effort failure details captured for a failed job.
/// </summary>
public sealed record JobFailureInfo(
    string ExceptionType,
    string Message,
    string? StackTrace);
