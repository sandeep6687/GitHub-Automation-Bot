using FluentAssertions;
using NetArchTest.Rules;

namespace GitHubBot.ArchitectureTests;

public class LayerDependencyTests
{
    private const string DomainNamespace = "GitHubBot.Domain";
    private const string ApplicationNamespace = "GitHubBot.Application";
    private const string InfrastructureNamespace = "GitHubBot.Infrastructure";
    private const string ApiNamespace = "GitHubBot.Api";

    [Fact]
    public void Domain_ShouldNotDependOn_Application_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(Domain.Interfaces.IUserRepository).Assembly)
            .ShouldNot()
            .HaveDependencyOnAll(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(Application.Services.RepositoryService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAll(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(typeof(Infrastructure.ExternalServices.GitHubApiClient).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Controllers_ShouldNotDependOn_Infrastructure()
    {
        // Controllers should rely on Application layer interfaces, not Infrastructure implementations
        var result = Types.InAssembly(typeof(Api.Controllers.RepositoryController).Assembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
