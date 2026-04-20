#pragma warning disable CA2000 // The contract context owns the created in-memory store for the test lifetime.

namespace Sheddueller.Tests.ProviderContracts;

using Sheddueller.Dashboard;
using Sheddueller.ProviderContracts;

public sealed class InMemoryDashboardContractTests : DashboardContractTests
{
    protected override ValueTask<DashboardContractContext> CreateContextAsync()
    {
        var store = new InMemoryJobStore();
        return ValueTask.FromResult(new DashboardContractContext(store, store, store, store));
    }
}
