using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GitHubBot.Application.DTOs.Repository;
using GitHubBot.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GitHubBot.IntegrationTests;

public class RepositoryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RepositoryIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAvailableRepositories_Unauthenticated_ShouldReturn401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/repositories/available");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAvailableRepositories_Authenticated_ShouldReturnRepos()
    {
        var mockRepoService = new Mock<IRepositoryService>();
        var sampleRepos = new List<AvailableRepoDto>
        {
            new() { Id = 1001, FullName = "test/repo1", Name = "repo1", Owner = "test", IsConnected = false },
            new() { Id = 1002, FullName = "test/repo2", Name = "repo2", Owner = "test", IsConnected = true }
        };

        mockRepoService
            .Setup(s => s.GetAvailableRepositoriesAsync(TestAuthHandler.TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleRepos);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockRepoService.Object);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/repositories/available");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var repos = await response.Content.ReadFromJsonAsync<List<AvailableRepoDto>>();

        repos.Should().NotBeNull();
        repos.Should().HaveCount(2);
        repos!.First().FullName.Should().Be("test/repo1");
    }

    [Fact]
    public async Task ConnectRepository_Authenticated_ShouldConnectAndReturnDto_WithoutSecret()
    {
        var mockRepoService = new Mock<IRepositoryService>();
        var connectedResult = new ConnectedRepoDto
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 556677,
            FullName = "octocat/hello-world",
            Name = "hello-world",
            Owner = "octocat",
            DefaultBranch = "main",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        mockRepoService
            .Setup(s => s.ConnectRepositoryAsync(TestAuthHandler.TestUserId, 556677, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectedResult);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockRepoService.Object);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/api/repositories/connect", new ConnectRepoRequest
        {
            GithubRepositoryId = 556677
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ConnectedRepoDto>();

        result.Should().NotBeNull();
        result!.GithubRepositoryId.Should().Be(556677);
        result.FullName.Should().Be("octocat/hello-world");

        // Security check: webhook secret is never returned in response JSON
        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("secret");
        rawJson.Should().NotContain("Secret");
    }

    [Fact]
    public async Task ConnectRepository_WhenAlreadyConnected_ShouldReturn409Conflict()
    {
        var mockRepoService = new Mock<IRepositoryService>();

        mockRepoService
            .Setup(s => s.ConnectRepositoryAsync(TestAuthHandler.TestUserId, 9999, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Repository is already connected."));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockRepoService.Object);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/api/repositories/connect", new ConnectRepoRequest
        {
            GithubRepositoryId = 9999
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("already connected");
    }
}
