using System.Net;
using FluentAssertions;
using GitHubBot.Application.Exceptions;
using GitHubBot.Infrastructure.ExternalServices;
using Moq;
using Moq.Protected;

namespace GitHubBot.UnitTests;

public class GitHubApiClientErrorHandlingTests
{
    private GitHubApiClient CreateClientWithResponse(HttpStatusCode statusCode, string responseContent = "{}")
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        return new GitHubApiClient(httpClient);
    }

    [Fact]
    public async Task AddLabelsAsync_WhenGitHubReturns401_ShouldThrowPermanentGitHubApiException()
    {
        // Arrange (Test 7: GitHub 401)
        var client = CreateClientWithResponse(HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");

        // Act
        var act = () => client.AddLabelsAsync("ghp_secret_token", "owner", "repo", 1, new[] { "bug" });

        // Assert
        var ex = await act.Should().ThrowAsync<GitHubApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.IsTransient.Should().BeFalse(); // Permanent error - do NOT retry
        ex.Which.Message.Should().NotContain("ghp_secret_token"); // Never log tokens!
    }

    [Fact]
    public async Task AddLabelsAsync_WhenGitHubReturns403_ShouldThrowPermanentGitHubApiException()
    {
        // Arrange (Test 8: GitHub 403)
        var client = CreateClientWithResponse(HttpStatusCode.Forbidden, "{\"message\":\"Resource not accessible by integration\"}");

        // Act
        var act = () => client.AddLabelsAsync("ghp_secret_token", "owner", "repo", 1, new[] { "bug" });

        // Assert
        var ex = await act.Should().ThrowAsync<GitHubApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Which.IsTransient.Should().BeFalse(); // Permanent error - do NOT retry
    }

    [Fact]
    public async Task AddCommentAsync_WhenGitHubReturns404_ShouldThrowPermanentGitHubApiException()
    {
        // Arrange (Test 9: GitHub 404)
        var client = CreateClientWithResponse(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");

        // Act
        var act = () => client.AddCommentAsync("ghp_secret_token", "owner", "repo", 999, "comment");

        // Assert
        var ex = await act.Should().ThrowAsync<GitHubApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.IsTransient.Should().BeFalse(); // Resource not found
    }

    [Fact]
    public async Task AddLabelsAsync_WhenGitHubReturns429_ShouldThrowTransientGitHubApiException()
    {
        // Arrange (Test 10: GitHub 429)
        var client = CreateClientWithResponse((HttpStatusCode)429, "{\"message\":\"Too Many Requests\"}");

        // Act
        var act = () => client.AddLabelsAsync("ghp_secret_token", "owner", "repo", 1, new[] { "bug" });

        // Assert
        var ex = await act.Should().ThrowAsync<GitHubApiException>();
        ex.Which.StatusCode.Should().Be((HttpStatusCode)429);
        ex.Which.IsTransient.Should().BeTrue(); // Transient rate limit - RETRYABLE
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task AddCommentAsync_WhenGitHubReturns5xx_ShouldThrowTransientGitHubApiException(HttpStatusCode serverError)
    {
        // Arrange (Test 11: GitHub 5xx)
        var client = CreateClientWithResponse(serverError, "{\"message\":\"Server Error\"}");

        // Act
        var act = () => client.AddCommentAsync("ghp_secret_token", "owner", "repo", 1, "comment");

        // Assert
        var ex = await act.Should().ThrowAsync<GitHubApiException>();
        ex.Which.StatusCode.Should().Be(serverError);
        ex.Which.IsTransient.Should().BeTrue(); // Transient 5xx - RETRYABLE
    }
}
