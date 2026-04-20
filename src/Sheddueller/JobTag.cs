namespace Sheddueller;

/// <summary>
/// Searchable domain metadata attached to a scheduled job.
/// </summary>
public sealed record JobTag(
    string Name,
    string Value);
