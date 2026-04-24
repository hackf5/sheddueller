namespace Sheddueller.Dashboard.Internal;

using Microsoft.AspNetCore.WebUtilities;

using Sheddueller.Inspection.ConcurrencyGroups;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Inspection.Nodes;
using Sheddueller.Inspection.Schedules;
using Sheddueller.Storage;

internal sealed class DashboardJobFilters
{
    private readonly HashSet<JobState> _states = [];
    private string _handlerContains = string.Empty;
    private string _tagContains = string.Empty;
    private string _concurrencyGroupContains = string.Empty;

    public string HandlerContains => this._handlerContains;

    public string TagContains => this._tagContains;

    public string ConcurrencyGroupContains => this._concurrencyGroupContains;

    public IReadOnlyList<JobState> SelectedStates
      => [.. Enum.GetValues<JobState>().Where(this._states.Contains)];

    public bool HasAppliedFilters
      => this._states.Count > 0
        || !string.IsNullOrWhiteSpace(this.HandlerContains)
        || !string.IsNullOrWhiteSpace(this.TagContains)
        || !string.IsNullOrWhiteSpace(this.ConcurrencyGroupContains);

    public bool IsStateSelected(JobState state)
      => this._states.Contains(state);

    public bool SetStateSelected(
        JobState state,
        bool selected)
      => selected ? this._states.Add(state) : this._states.Remove(state);

    public bool SetHandlerContains(string value)
      => SetTextFilter(ref this._handlerContains, value);

    public bool SetTagContains(string value)
      => SetTextFilter(ref this._tagContains, value);

    public bool SetConcurrencyGroupContains(string value)
      => SetTextFilter(ref this._concurrencyGroupContains, value);

    public bool ReplaceStates(IEnumerable<JobState> states)
    {
        var nextStates = states.ToHashSet();
        if (this._states.SetEquals(nextStates))
        {
            return false;
        }

        this._states.Clear();
        foreach (var state in nextStates)
        {
            this._states.Add(state);
        }

        return true;
    }

    public bool ReplaceWith(DashboardJobFilters filters)
    {
        var changed = !this.SelectedStates.SequenceEqual(filters.SelectedStates)
          || !string.Equals(this.HandlerContains, filters.HandlerContains, StringComparison.Ordinal)
          || !string.Equals(this.TagContains, filters.TagContains, StringComparison.Ordinal)
          || !string.Equals(this.ConcurrencyGroupContains, filters.ConcurrencyGroupContains, StringComparison.Ordinal);

        if (!changed)
        {
            return false;
        }

        this._states.Clear();
        foreach (var state in filters.SelectedStates)
        {
            this._states.Add(state);
        }

        this._handlerContains = filters.HandlerContains;
        this._tagContains = filters.TagContains;
        this._concurrencyGroupContains = filters.ConcurrencyGroupContains;
        return true;
    }

    public DashboardJobFilters Clone()
    {
        var clone = new DashboardJobFilters();
        _ = clone.ReplaceWith(this);
        return clone;
    }

    public void Clear()
    {
        this._states.Clear();
        this._handlerContains = string.Empty;
        this._tagContains = string.Empty;
        this._concurrencyGroupContains = string.Empty;
    }

    public JobInspectionQuery ToQuery(
        int pageSize,
        string? continuationToken)
      => new(
        this._states.Count == 0 ? null : [.. Enum.GetValues<JobState>().Where(this._states.Contains)],
        Normalize(this.HandlerContains),
        Normalize(this.TagContains),
        Normalize(this.ConcurrencyGroupContains),
        pageSize,
        continuationToken);

    private static bool SetTextFilter(
        ref string field,
        string value)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return false;
        }

        field = value;
        return true;
    }

    private static string? Normalize(string value)
      => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class DashboardJobFilterQuery
{
    public const string StateParameter = "state";
    public const string HandlerParameter = "handler";
    public const string TagParameter = "tag";
    public const string GroupParameter = "group";

    private const string JobsPath = "jobs";

    public static DashboardJobFilters ParseUri(string uri)
      => ParseQuery(new Uri(uri, UriKind.Absolute).Query);

    public static DashboardJobFilters ParseQuery(string queryString)
    {
        var filters = new DashboardJobFilters();
        var query = QueryHelpers.ParseQuery(queryString);

        if (query.TryGetValue(StateParameter, out var stateValues))
        {
            var states = new HashSet<JobState>();
            foreach (var stateValue in stateValues)
            {
                if (Enum.TryParse<JobState>(stateValue, ignoreCase: true, out var state))
                {
                    states.Add(state);
                }
            }

            _ = filters.ReplaceStates(states);
        }

        if (TryGetLastNonEmptyValue(query, HandlerParameter, out var handler))
        {
            _ = filters.SetHandlerContains(handler);
        }

        if (TryGetLastNonEmptyValue(query, TagParameter, out var tag))
        {
            _ = filters.SetTagContains(tag);
        }

        if (TryGetLastNonEmptyValue(query, GroupParameter, out var group))
        {
            _ = filters.SetConcurrencyGroupContains(group);
        }

        return filters;
    }

    public static string ToHref(DashboardJobFilters filters)
    {
        var queryParts = new List<string>();

        foreach (var state in filters.SelectedStates)
        {
            AddQueryPart(queryParts, StateParameter, state.ToString());
        }

        AddQueryPart(queryParts, HandlerParameter, filters.HandlerContains);
        AddQueryPart(queryParts, TagParameter, filters.TagContains);
        AddQueryPart(queryParts, GroupParameter, filters.ConcurrencyGroupContains);

        return queryParts.Count == 0
          ? JobsPath
          : string.Concat(JobsPath, "?", string.Join("&", queryParts));
    }

    public static string StateHref(JobState state)
    {
        var filters = new DashboardJobFilters();
        _ = filters.ReplaceStates([state]);
        return ToHref(filters);
    }

    public static string HandlerHref(string handler)
    {
        var filters = new DashboardJobFilters();
        _ = filters.SetHandlerContains(handler);
        return ToHref(filters);
    }

    public static string TagHref(string tag)
    {
        var filters = new DashboardJobFilters();
        _ = filters.SetTagContains(tag);
        return ToHref(filters);
    }

    public static string GroupHref(string group)
    {
        var filters = new DashboardJobFilters();
        _ = filters.SetConcurrencyGroupContains(group);
        return ToHref(filters);
    }

    public static string WithStateHref(
        DashboardJobFilters filters,
        JobState state)
    {
        var next = filters.Clone();
        _ = next.ReplaceStates([state]);
        return ToHref(next);
    }

    public static string WithHandlerHref(
        DashboardJobFilters filters,
        string handler)
    {
        var next = filters.Clone();
        _ = next.SetHandlerContains(handler);
        return ToHref(next);
    }

    public static string WithTagHref(
        DashboardJobFilters filters,
        string tag)
    {
        var next = filters.Clone();
        _ = next.SetTagContains(tag);
        return ToHref(next);
    }

    public static string WithGroupHref(
        DashboardJobFilters filters,
        string group)
    {
        var next = filters.Clone();
        _ = next.SetConcurrencyGroupContains(group);
        return ToHref(next);
    }

    private static bool TryGetLastNonEmptyValue(
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query,
        string parameterName,
        out string value)
    {
        value = string.Empty;
        if (!query.TryGetValue(parameterName, out var values))
        {
            return false;
        }

        for (var i = values.Count - 1; i >= 0; i--)
        {
            var candidate = values[i];
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate.Trim();
                return true;
            }
        }

        return false;
    }

    private static void AddQueryPart(
        List<string> queryParts,
        string parameterName,
        string value)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalizedValue is null)
        {
            return;
        }

        queryParts.Add(string.Concat(
          Uri.EscapeDataString(parameterName),
          "=",
          Uri.EscapeDataString(normalizedValue)));
    }
}

internal sealed class DashboardScheduleFilters
{
    public const string ActiveStateFilter = "active";
    public const string PausedStateFilter = "paused";

    public string ScheduleKey { get; set; } = string.Empty;

    public string PauseState { get; set; } = string.Empty;

    public bool HasAppliedFilters
      => !string.IsNullOrWhiteSpace(this.ScheduleKey)
        || !string.IsNullOrWhiteSpace(this.PauseState);

    public ScheduleInspectionQuery ToQuery(
        int pageSize,
        string? continuationToken)
      => new(
        Normalize(this.ScheduleKey),
        ParsePauseState(this.PauseState),
        ServiceType: null,
        MethodName: null,
        Tag: null,
        pageSize,
        continuationToken);

    private static bool? ParsePauseState(string value)
      => value switch
      {
          PausedStateFilter => true,
          ActiveStateFilter => false,
          _ => null,
      };

    private static string? Normalize(string value)
      => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class DashboardNodeFilters
{
    public string State { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public NodeHealthState? StateFilter
      => Enum.TryParse<NodeHealthState>(this.State, out var state) ? state : null;

    public IReadOnlyList<NodeInspectionSummary> ApplyClientFilter(IReadOnlyList<NodeInspectionSummary> nodes)
      => string.IsNullOrWhiteSpace(this.NodeId)
        ? nodes
        : [.. nodes.Where(node => node.NodeId.Contains(this.NodeId, StringComparison.OrdinalIgnoreCase))];

    public NodeInspectionQuery ToQuery(
        int pageSize,
        string? continuationToken)
      => new(this.StateFilter, pageSize, continuationToken);
}

internal sealed class DashboardConcurrencyGroupFilters
{
    public string GroupKey { get; set; } = string.Empty;

    public bool SaturatedOnly { get; set; }

    public bool HasBlockedJobsOnly { get; set; }

    public bool? SaturatedFilter
      => this.SaturatedOnly ? true : null;

    public bool? HasBlockedJobsFilter
      => this.HasBlockedJobsOnly ? true : null;

    public IReadOnlyList<ConcurrencyGroupInspectionSummary> ApplyClientFilter(
        IReadOnlyList<ConcurrencyGroupInspectionSummary> groups)
      => string.IsNullOrWhiteSpace(this.GroupKey)
        ? groups
        : [.. groups.Where(group => group.GroupKey.Contains(this.GroupKey, StringComparison.OrdinalIgnoreCase))];

    public ConcurrencyGroupInspectionQuery ToQuery(
        int pageSize,
        string? continuationToken)
      => new(
        GroupKey: null,
        IsSaturated: this.SaturatedFilter,
        HasBlockedJobs: this.HasBlockedJobsFilter,
        PageSize: pageSize,
        ContinuationToken: continuationToken);
}
