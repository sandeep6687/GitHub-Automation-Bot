using System.Net;
using FluentAssertions;
using GitHubBot.Application.Configuration;
using GitHubBot.Application.Exceptions;
using GitHubBot.Infrastructure.ExternalServices;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace GitHubBot.UnitTests;

public class SlackApiClientTests
{
    private const string SecretWebhookUrl = "https://hooks.slack.com/services/T000/B000/FAKE_SECRET_KEY_FOR_TESTING";

    private (SlackApiClient Client, Mock<HttpMessageHandler> HandlerMock) CreateClientWithResponse(
        HttpStatusCode statusCode,
        string responseBody = "ok")
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
                Content = new StringContent(responseBody)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var options = Options.Create(new SlackOptions { WebhookUrl = SecretWebhookUrl });
        var client = new SlackApiClient(httpClient, options);

        return (client, handlerMock);
    }

    [Fact]
    public async Task SendMessageAsync_WhenSlackReturns200_ShouldSucceed()
    {
        // Arrange (Test 13: Slack 2xx -> success)
        var (client, handlerMock) = CreateClientWithResponse(HttpStatusCode.OK, "ok");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        await act.Should().NotThrowAsync();

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.Content != null &&
                req.Content.Headers.ContentType!.MediaType == "application/json"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendMessageAsync_WhenSlackReturns400_ShouldThrowPermanentSlackApiException()
    {
        // Arrange (Test 14: Slack 400 -> permanent failure)
        var (client, _) = CreateClientWithResponse(HttpStatusCode.BadRequest, "invalid_payload");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Which.IsTransient.Should().BeFalse(); // Permanent
        ex.Which.Message.Should().NotContain(SecretWebhookUrl); // Secret URL never exposed
    }

    [Fact]
    public async Task SendMessageAsync_WhenSlackReturns401_ShouldThrowPermanentSlackApiException()
    {
        // Arrange (Test 15: Slack 401 -> permanent failure)
        var (client, _) = CreateClientWithResponse(HttpStatusCode.Unauthorized, "invalid_token");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.IsTransient.Should().BeFalse(); // Permanent
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Fact]
    public async Task SendMessageAsync_WhenSlackReturns403_ShouldThrowPermanentSlackApiException()
    {
        // Arrange (Test 16: Slack 403 -> permanent failure)
        var (client, _) = CreateClientWithResponse(HttpStatusCode.Forbidden, "action_prohibited");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Which.IsTransient.Should().BeFalse(); // Permanent
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Fact]
    public async Task SendMessageAsync_WhenSlackReturns404_ShouldThrowPermanentSlackApiException()
    {
        // Arrange (Test 17: Slack 404 -> permanent failure)
        var (client, _) = CreateClientWithResponse(HttpStatusCode.NotFound, "channel_not_found");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.IsTransient.Should().BeFalse(); // Permanent
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Fact]
    public async Task SendMessageAsync_WhenSlackReturns429_ShouldThrowTransientSlackApiException()
    {
        // Arrange (Test 18: Slack 429 -> transient failure)
        var (client, _) = CreateClientWithResponse((HttpStatusCode)429, "rate_limited");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.StatusCode.Should().Be((HttpStatusCode)429);
        ex.Which.IsTransient.Should().BeTrue(); // Transient -> retry
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task SendMessageAsync_WhenSlackReturns5xx_ShouldThrowTransientSlackApiException(HttpStatusCode code)
    {
        // Arrange (Test 19: Slack 5xx -> transient failure)
        var (client, _) = CreateClientWithResponse(code, "server_error");

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.StatusCode.Should().Be(code);
        ex.Which.IsTransient.Should().BeTrue(); // Transient -> retry
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Fact]
    public async Task SendMessageAsync_WhenRequestTimesOut_ShouldThrowTransientSlackApiException()
    {
        // Arrange (Test 20: Timeout -> transient failure)
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("HttpClient timeout"));

        var httpClient = new HttpClient(handlerMock.Object);
        var options = Options.Create(new SlackOptions { WebhookUrl = SecretWebhookUrl });
        var client = new SlackApiClient(httpClient, options);

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.IsTransient.Should().BeTrue();
        ex.Which.Message.Should().Contain("timed out");
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Fact]
    public async Task SendMessageAsync_WhenNetworkFails_ShouldThrowTransientSlackApiException()
    {
        // Arrange (Test 21: Network failure -> transient failure)
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused by peer"));

        var httpClient = new HttpClient(handlerMock.Object);
        var options = Options.Create(new SlackOptions { WebhookUrl = SecretWebhookUrl });
        var client = new SlackApiClient(httpClient, options);

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.IsTransient.Should().BeTrue();
        ex.Which.Message.Should().Contain("Failed to connect");
        ex.Which.Message.Should().NotContain(SecretWebhookUrl);
    }

    [Fact]
    public async Task SendMessageAsync_WhenWebhookUrlIsNotConfigured_ShouldThrowPermanentFailureWithoutLeakingSecrets()
    {
        // Arrange (Test 22: Slack webhook URL never appears in logs/errors/results)
        var httpClient = new HttpClient();
        var options = Options.Create(new SlackOptions { WebhookUrl = null });
        var client = new SlackApiClient(httpClient, options);

        // Act
        var act = () => client.SendMessageAsync("Hello Slack!");

        // Assert
        var ex = await act.Should().ThrowAsync<SlackApiException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.Message.Should().Contain("Slack Webhook URL is not configured");
    }
}
