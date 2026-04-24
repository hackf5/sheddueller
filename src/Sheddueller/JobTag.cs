namespace Sheddueller;

/// <summary>
/// Searchable domain metadata attached to a scheduled job.
/// </summary>
/// <param name="Name">The tag name.</param>
/// <param name="Value">The tag value.</param>
public sealed record JobTag(
    string Name,
    string Value);
