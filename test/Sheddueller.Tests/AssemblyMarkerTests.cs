using Shouldly;

namespace Sheddueller.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void AssemblyMarkerLivesInShedduellerAssembly()
    {
        var assemblyName = typeof(AssemblyMarker).Assembly.GetName().Name;

        assemblyName.ShouldBe("Sheddueller");
    }
}
