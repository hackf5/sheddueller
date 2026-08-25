namespace Sheddueller.Dashboard;

/// <summary>
/// Configures one visible column in a shared dashboard job view.
/// </summary>
/// <param name="Kind">The kind of column to render.</param>
/// <param name="TagName">The tag name to extract when <paramref name="Kind" /> is <see cref="ShedduellerDashboardJobColumnKind.Tag" />.</param>
/// <param name="Heading">An optional heading for a tag column. The tag name is used when omitted.</param>
public sealed record ShedduellerDashboardJobColumn(
    ShedduellerDashboardJobColumnKind Kind,
    string? TagName = null,
    string? Heading = null);
