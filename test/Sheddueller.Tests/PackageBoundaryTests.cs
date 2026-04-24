namespace Sheddueller.Tests;

using Shouldly;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void DashboardAndPostgres_DoNotReferenceEachOther()
    {
        var root = FindRepositoryRoot();
        var dashboardProject = File.ReadAllText(Path.Combine(root, "src", "Sheddueller.Dashboard", "Sheddueller.Dashboard.csproj"));
        var postgresProject = File.ReadAllText(Path.Combine(root, "src", "Sheddueller.Postgres", "Sheddueller.Postgres.csproj"));

        dashboardProject.ShouldNotContain("Sheddueller.Postgres");
        postgresProject.ShouldNotContain("Sheddueller.Dashboard");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sheddueller.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
          ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
