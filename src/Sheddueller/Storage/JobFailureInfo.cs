namespace Sheddueller.Storage;

/// <summary>
/// Best-effort failure details captured for a failed task.
/// </summary>
public sealed record TaskFailureInfo(
    string ExceptionType,
    string Message,
    string? StackTrace);
