namespace Sheddueller.Dashboard.Internal;

internal static class DashboardTagOrder
{
    public static bool IsValid(ShedduellerDashboardOptions options)
      => IsValid(options.TagDisplayOrder);

    public static bool IsValid(IReadOnlyList<string>? tagDisplayOrder)
    {
        if (tagDisplayOrder is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tagName in tagDisplayOrder)
        {
            if (tagName is null)
            {
                return false;
            }

            var normalized = tagName.Trim();
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<JobTag> Apply(
        IReadOnlyList<JobTag> tags,
        IReadOnlyList<string>? tagDisplayOrder)
    {
        if (tags.Count == 0 || tagDisplayOrder is null || tagDisplayOrder.Count == 0)
        {
            return tags;
        }

        var ranks = CreateRanks(tagDisplayOrder);
        if (ranks.Count == 0)
        {
            return tags;
        }

        return
        [
            .. tags
              .Select((tag, index) => new OrderedTag(tag, index, GetRank(ranks, tag)))
              .OrderBy(tag => tag.Rank)
              .ThenBy(tag => tag.OriginalIndex)
              .Select(tag => tag.Tag),
        ];
    }

    private static Dictionary<string, int> CreateRanks(IReadOnlyList<string> tagDisplayOrder)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tagName in tagDisplayOrder)
        {
            if (tagName is null)
            {
                continue;
            }

            var normalized = tagName.Trim();
            if (normalized.Length > 0 && !ranks.ContainsKey(normalized))
            {
                ranks.Add(normalized, ranks.Count);
            }
        }

        return ranks;
    }

    private static int GetRank(
        Dictionary<string, int> ranks,
        JobTag tag)
      => ranks.TryGetValue(tag.Name.Trim(), out var rank) ? rank : int.MaxValue;

    private sealed record OrderedTag(
        JobTag Tag,
        int OriginalIndex,
        int Rank);
}
