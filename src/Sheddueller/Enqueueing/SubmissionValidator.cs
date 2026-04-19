#pragma warning disable IDE0130

namespace Sheddueller;

internal static class SubmissionValidator
{
    public static IReadOnlyList<string> NormalizeConcurrencyGroupKeys(IReadOnlyList<string>? groupKeys)
    {
        if (groupKeys is null || groupKeys.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(groupKeys.Count);

        foreach (var groupKey in groupKeys)
        {
            ValidateConcurrencyGroupKey(groupKey);

            if (seen.Add(groupKey))
            {
                normalized.Add(groupKey);
            }
        }

        return normalized;
    }

    public static void ValidateConcurrencyGroupKey(string? groupKey)
    {
        if (string.IsNullOrEmpty(groupKey))
        {
            throw new ArgumentException("Concurrency group keys must be non-empty strings.", nameof(groupKey));
        }
    }
}
