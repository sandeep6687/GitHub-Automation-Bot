using GitHubBot.Domain;
using GitHubBot.Application;
using GitHubBot.Infrastructure;

namespace GitHubBot.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_ShouldHaveNoExternalDependencies()
    {
        // Domain assembly must not reference EF Core, HttpClient, or any NuGet package.
        var domainAssembly = typeof(DomainAssemblyMarker).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        // Domain may reference System.* and netstandard only
        var forbidden = referencedAssemblies
            .Where(name =>
                name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Newtonsoft", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Polly", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Application_ShouldNotReferenceInfrastructure()
    {
        var appAssembly = typeof(ApplicationAssemblyMarker).Assembly;
        var referencedAssemblies = appAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain("GitHubBot.Infrastructure", referencedAssemblies);
    }

    [Fact]
    public void Application_ShouldReferenceDomain()
    {
        var appAssembly = typeof(ApplicationAssemblyMarker).Assembly;
        var referencedAssemblies = appAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.Contains("GitHubBot.Domain", referencedAssemblies);
    }

    [Fact]
    public void Infrastructure_ShouldReferenceDomainAndApplication()
    {
        var infraAssembly = typeof(InfrastructureAssemblyMarker).Assembly;
        var referencedAssemblies = infraAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.Contains("GitHubBot.Domain", referencedAssemblies);
        Assert.Contains("GitHubBot.Application", referencedAssemblies);
    }
}
