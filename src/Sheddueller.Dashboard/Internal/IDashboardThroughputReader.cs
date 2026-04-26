namespace Sheddueller.Dashboard.Internal;

internal interface IDashboardThroughputReader
{
    DashboardThroughputSnapshot GetSnapshot();
}
