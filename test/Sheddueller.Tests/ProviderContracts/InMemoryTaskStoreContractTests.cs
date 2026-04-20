#pragma warning disable CA2000 // The contract context owns the created in-memory store for the test lifetime.

namespace Sheddueller.Tests.ProviderContracts;

using Sheddueller.ProviderContracts;

public sealed class InMemoryTaskStoreContractTests : TaskStoreContractTests
{
    protected override ValueTask<TaskStoreContractContext> CreateContextAsync()
      => ValueTask.FromResult(new TaskStoreContractContext(new InMemoryTaskStore()));
}
