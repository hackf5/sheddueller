namespace Sheddueller.Tests;

using Shouldly;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void AssemblyMarkerLivesInShedduellerAssembly()
    {
        var assemblyName = typeof(AssemblyMarker).Assembly.GetName().Name;

        assemblyName.ShouldBe("Sheddueller");
    }
}
