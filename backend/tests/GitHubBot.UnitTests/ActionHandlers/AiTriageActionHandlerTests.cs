using System.Net;
using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.ActionHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace GitHubBot.UnitTests.ActionHandlers;

public class AiTriageActionHandlerTests
{
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<AiTriageActionHandler>> _loggerMock;
    private readonly ActionContext _context;

    public AiTriageActionHandlerTests()
    {
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<AiTriageActionHandler>>();

        var webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            EventType = "issues",
            RawPayload = "{ \"issue\": { \"title\": \"Bug here\", \"body\": \"It crashes.\", \"user\": { \"login\": \"testuser\" } } }"
        };

        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.AiTriage
        };

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 123
        };

        _context = new ActionContext(webhookEvent, ruleAction, repo, 1, 1);
    }

    [Fact]
    public async Task ExecuteAsync_MissingApiKey_ReturnsFailed()
    {
        _configMock.Setup(c => c["Gemini:ApiKey"]).Returns(string.Empty);
        var handler = new AiTriageActionHandler(new HttpClient(), _configMock.Object, _loggerMock.Object);

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Gemini API key is not configured");
        result.IsTransientError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ApiError_ReturnsTransientFailure()
    {
        _configMock.Setup(c => c["Gemini:ApiKey"]).Returns("fake-key");

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("error")
            });

        var client = new HttpClient(handlerMock.Object);
        var handler = new AiTriageActionHandler(client, _configMock.Object, _loggerMock.Object);

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("AI Provider failed");
        result.IsTransientError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsPayload()
    {
        _configMock.Setup(c => c["Gemini:ApiKey"]).Returns("fake-key");

        var aiResponse = new
        {
            candidates = new[]
            {
                new {
                    content = new {
                        parts = new[] {
                            new { text = "{ \"summary\": \"App crashes\", \"category\": \"Bug\", \"severity\": \"High\" }" }
                        }
                    }
                }
            }
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(aiResponse))
            });

        var client = new HttpClient(handlerMock.Object);
        var handler = new AiTriageActionHandler(client, _configMock.Object, _loggerMock.Object);

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeTrue();
        result.ResponsePayload.Should().Contain("summary");
        result.ResponsePayload.Should().Contain("App crashes");
    }

    [Fact]
    public async Task ExecuteAsync_MalformedJsonResponse_ReturnsTransientFailure()
    {
        _configMock.Setup(c => c["Gemini:ApiKey"]).Returns("fake-key");

        var aiResponse = new
        {
            candidates = new[]
            {
                new {
                    content = new {
                        parts = new[] {
                            new { text = "Not JSON format text" }
                        }
                    }
                }
            }
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(aiResponse))
            });

        var client = new HttpClient(handlerMock.Object);
        var handler = new AiTriageActionHandler(client, _configMock.Object, _loggerMock.Object);

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("parsing error");
    }
}
