namespace Sheddueller.Dashboard;

/// <summary>
/// Options for the embedded Sheddueller dashboard.
/// </summary>
public sealed class ShedduellerDashboardOptions
{
    /// <summary>
    /// Gets or sets whether dashboard routes are prerendered into the initial HTTP response.
    /// Defaults to <see langword="false" />.
    /// Prerendering can conflict with certain browser extensions, such as React Developer Tools,
    /// and may cause the dashboard to fail to load.
    /// </summary>
    public bool Prerender { get; set; }

    /// <summary>
    /// Gets or sets how long job events are retained after their owning job reaches a terminal state.
    /// </summary>
    public TimeSpan EventRetention { get; set; } = TimeSpan.FromDays(7);
}
