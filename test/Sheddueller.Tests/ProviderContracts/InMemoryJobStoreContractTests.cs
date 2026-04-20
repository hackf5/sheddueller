#pragma warning disable CA2000 // The contract context owns the created in-memory store for the test lifetime.

namespace Sheddueller.Tests.ProviderContracts;

using Sheddueller.ProviderContracts;

public sealed class InMemoryJobStoreContractTests : JobStoreContractTests
{
    protected override ValueTask<JobStoreContractContext> CreateContextAsync()
      => ValueTask.FromResult(new JobStoreContractContext(new InMemoryJobStore()));
}
