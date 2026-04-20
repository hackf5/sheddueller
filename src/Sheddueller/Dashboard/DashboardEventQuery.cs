namespace Sheddueller.Dashboard;

/// <summary>
/// Dashboard event read query.
/// </summary>
public sealed record DashboardEventQuery(
    long? AfterEventSequence = null,
    int Limit = 500);
