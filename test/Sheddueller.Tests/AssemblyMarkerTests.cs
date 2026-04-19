namespace Sheddueller.Tests;

using Shouldly;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void AssemblyMarker_Default_LivesInShedduellerAssembly()
    {
        var assemblyName = typeof(AssemblyMarker).Assembly.GetName().Name;

        assemblyName.ShouldBe("Sheddueller");
    }
}
