#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Best-effort failure details captured for a failed task.
/// </summary>
public sealed record TaskFailureInfo(
    string ExceptionType,
    string Message,
    string? StackTrace);
