namespace Sheddueller.Dashboard;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Storage;

/// <summary>
/// Configures a shared, read-only dashboard job view.
/// </summary>
/// <param name="Name">The user-facing view name.</param>
public sealed record ShedduellerDashboardJobView(string Name)
{
    /// <summary>
    /// Gets the job states included by the view. An empty list includes every state.
    /// </summary>
    public IReadOnlyList<JobState> States { get; init; } = [];

    /// <summary>
    /// Gets the optional handler substring filter.
    /// </summary>
    public string? HandlerContains { get; init; }

    /// <summary>
    /// Gets the optional tag substring filter.
    /// </summary>
    public string? TagContains { get; init; }

    /// <summary>
    /// Gets the optional concurrency group substring filter.
    /// </summary>
    public string? ConcurrencyGroupContains { get; init; }

    /// <summary>
    /// Gets the job sort applied by the view.
    /// </summary>
    public JobInspectionSort Sort { get; init; } = JobInspectionSort.Operational;

    /// <summary>
    /// Gets the ordered visible columns. A <see langword="null" /> value uses the built-in column layout.
    /// </summary>
    public IReadOnlyList<ShedduellerDashboardJobColumn>? Columns { get; init; }
}
