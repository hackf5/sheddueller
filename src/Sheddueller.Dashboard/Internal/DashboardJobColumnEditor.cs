namespace Sheddueller.Dashboard.Internal;

internal static class DashboardJobColumnEditor
{
    public static bool Move(
        List<ShedduellerDashboardJobColumn> columns,
        int fromIndex,
        int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= columns.Count
            || toIndex < 0 || toIndex >= columns.Count
            || fromIndex == toIndex)
        {
            return false;
        }

        var column = columns[fromIndex];
        columns.RemoveAt(fromIndex);
        columns.Insert(toIndex, column);
        return true;
    }

    public static bool SetBuiltInVisibility(
        List<ShedduellerDashboardJobColumn> columns,
        ShedduellerDashboardJobColumnKind kind,
        bool visible)
    {
        if (kind == ShedduellerDashboardJobColumnKind.Tag)
        {
            return false;
        }

        var index = columns.FindIndex(column => column.Kind == kind);
        if (visible && index < 0)
        {
            columns.Add(new ShedduellerDashboardJobColumn(kind));
            return true;
        }

        if (!visible && index >= 0 && kind != ShedduellerDashboardJobColumnKind.JobId)
        {
            columns.RemoveAt(index);
            return true;
        }

        return false;
    }

    public static bool TryAddTag(
        List<ShedduellerDashboardJobColumn> columns,
        string tagName,
        string? heading,
        out string? error)
    {
        if (!TryCreateTag(columns, exceptIndex: null, tagName, heading, out var column, out error))
        {
            return false;
        }

        columns.Add(column!);
        return true;
    }

    public static bool TryUpdateTag(
        List<ShedduellerDashboardJobColumn> columns,
        int index,
        string tagName,
        string? heading,
        out string? error)
    {
        if (index < 0 || index >= columns.Count
            || columns[index].Kind != ShedduellerDashboardJobColumnKind.Tag)
        {
            error = "The promoted tag column no longer exists.";
            return false;
        }

        if (!TryCreateTag(columns, index, tagName, heading, out var column, out error))
        {
            return false;
        }

        columns[index] = column!;
        return true;
    }

    public static bool RemoveTag(
        List<ShedduellerDashboardJobColumn> columns,
        int index)
    {
        if (index < 0 || index >= columns.Count
            || columns[index].Kind != ShedduellerDashboardJobColumnKind.Tag)
        {
            return false;
        }

        columns.RemoveAt(index);
        return true;
    }

    private static bool TryCreateTag(
        List<ShedduellerDashboardJobColumn> columns,
        int? exceptIndex,
        string tagName,
        string? heading,
        out ShedduellerDashboardJobColumn? column,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            column = null;
            error = "Enter a tag name to promote.";
            return false;
        }

        var normalizedName = tagName.Trim();
        if (columns.Select((candidate, index) => (candidate, index)).Any(item =>
            item.index != exceptIndex
            && item.candidate.Kind == ShedduellerDashboardJobColumnKind.Tag
            && string.Equals(item.candidate.TagName, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            column = null;
            error = "That tag already has a promoted column.";
            return false;
        }

        column = new ShedduellerDashboardJobColumn(
          ShedduellerDashboardJobColumnKind.Tag,
          normalizedName,
          string.IsNullOrWhiteSpace(heading) ? null : heading.Trim())
        {
            Width = exceptIndex is { } index ? columns[index].Width : null,
        };
        error = null;
        return true;
    }
}
