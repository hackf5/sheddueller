namespace Sheddueller.Dashboard;

/// <summary>
/// Options for the embedded Sheddueller dashboard.
/// </summary>
public sealed class ShedduellerDashboardOptions
{
    /// <summary>
    /// Gets or sets how long job events are retained after their owning job reaches a terminal state.
    /// </summary>
    public TimeSpan EventRetention { get; set; } = TimeSpan.FromDays(7);
}
