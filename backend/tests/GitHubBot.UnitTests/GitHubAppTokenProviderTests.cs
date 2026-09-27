using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using GitHubBot.Application.Configuration;
using GitHubBot.Infrastructure.ExternalServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace GitHubBot.UnitTests;

public class GitHubAppTokenProviderTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IOptions<GitHubAppOptions>> _optionsMock;
    private readonly GitHubAppOptions _options;

    public GitHubAppTokenProviderTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _optionsMock = new Mock<IOptions<GitHubAppOptions>>();
        
        using var rsa = RSA.Create();
        _options = new GitHubAppOptions
        {
            AppId = 12345,
            PrivateKey = rsa.ExportRSAPrivateKeyPem()
        };
        _optionsMock.Setup(x => x.Value).Returns(_options);
    }

    [Fact]
    public async Task GetInstallationTokenAsync_ShouldGenerateToken_AndCacheIt()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Created,
                Content = new StringContent("{\"token\":\"ghs_fake_token\",\"expires_at\":\"2026-10-10T10:00:00Z\"}")
            })
            .Verifiable();

        var provider = new GitHubAppTokenProvider(_httpClient, _memoryCache, _optionsMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAppTokenProvider>.Instance);

        // Act
        var token1 = await provider.GetInstallationTokenAsync(999);
        var token2 = await provider.GetInstallationTokenAsync(999); // Should hit cache

        // Assert
        Assert.Equal("ghs_fake_token", token1);
        Assert.Equal("ghs_fake_token", token2);
        
        // Verify HTTP client was called exactly once due to caching
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task TryGetGitHubAppInstallationIdAsync_ShouldReturnId_When200OK()
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"id\": 165544798}")
            });

        var provider = new GitHubAppTokenProvider(_httpClient, _memoryCache, _optionsMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAppTokenProvider>.Instance);
        var result = await provider.TryGetGitHubAppInstallationIdAsync("owner", "repo");

        Assert.Equal(165544798, result);
    }

    [Fact]
    public async Task TryGetGitHubAppInstallationIdAsync_ShouldReturnNull_When404NotFound()
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        var provider = new GitHubAppTokenProvider(_httpClient, _memoryCache, _optionsMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAppTokenProvider>.Instance);
        var result = await provider.TryGetGitHubAppInstallationIdAsync("owner", "repo");

        Assert.Null(result); // Must NOT throw exception
    }

    [Fact]
    public async Task TryGetGitHubAppInstallationIdAsync_ShouldThrowInvalidOperationException_WhenAppIdMissing()
    {
        _options.AppId = null;
        var provider = new GitHubAppTokenProvider(_httpClient, _memoryCache, _optionsMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAppTokenProvider>.Instance);
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TryGetGitHubAppInstallationIdAsync("owner", "repo"));
        Assert.Contains("Missing AppId or PrivateKey", ex.Message);
    }

    [Fact]
    public async Task TryGetGitHubAppInstallationIdAsync_ShouldThrowInvalidOperationException_WhenPrivateKeyMissing()
    {
        _options.PrivateKey = null;
        var provider = new GitHubAppTokenProvider(_httpClient, _memoryCache, _optionsMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAppTokenProvider>.Instance);
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TryGetGitHubAppInstallationIdAsync("owner", "repo"));
        Assert.Contains("Missing AppId or PrivateKey", ex.Message);
    }

    [Fact]
    public async Task TryGetGitHubAppInstallationIdAsync_ShouldReturnNull_When500InternalServerError()
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        var provider = new GitHubAppTokenProvider(_httpClient, _memoryCache, _optionsMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAppTokenProvider>.Instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.TryGetGitHubAppInstallationIdAsync("owner", "repo"));
    }
}
