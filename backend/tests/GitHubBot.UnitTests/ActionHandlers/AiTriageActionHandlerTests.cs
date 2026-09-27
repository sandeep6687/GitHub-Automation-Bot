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

/// <summary>
/// Unit tests for AiTriageActionHandler.
/// 
/// Key invariants tested:
/// - Max 2 Gemini HTTP calls per execution (primary + at-most-one fallback).
/// - x-goog-api-key header used; API key never appears in URL or stored payloads.
/// - 429/500/502/503/504 → transient failure → fallback attempted.
/// - 400/401/403/404/422 → permanent failure → fallback NOT attempted.
/// - Successful previous actions are unaffected by AI retry semantics (idempotency
///   is handled by ActionDispatcher / ActionExecution — tested in ActionDispatcherTests).
/// </summary>
public class AiTriageActionHandlerTests
{
    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private const string FakeApiKey = "fake-key-never-logged";
    private const string PrimaryModelName = "gemini-3.8-flash";
    private const string FallbackModelName = "gemini-3.5-flash-lite";

    private readonly Mock<ILogger<AiTriageActionHandler>> _loggerMock = new();
    private readonly ActionContext _context;

    public AiTriageActionHandlerTests()
    {
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

    /// <summary>Builds a handler whose HTTP client returns the specified sequence of responses (one per call).</summary>
    private AiTriageActionHandler BuildHandler(IConfiguration config, params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => queue.Dequeue());

        var client = new HttpClient(handlerMock.Object);
        return new AiTriageActionHandler(client, config, _loggerMock.Object);
    }

    private static IConfiguration MakeConfig(
        string apiKey = FakeApiKey,
        string primaryModel = PrimaryModelName,
        string fallbackModel = FallbackModelName)
    {
        var mock = new Mock<IConfiguration>();
        mock.Setup(c => c["Gemini:ApiKey"]).Returns(apiKey);
        mock.Setup(c => c["Gemini:PrimaryModel"]).Returns(primaryModel);
        mock.Setup(c => c["Gemini:FallbackModel"]).Returns(fallbackModel);
        return mock.Object;
    }

    private static string GeminiOkJson(string summary = "App crashes due to null ref") =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = $"{{\"summary\":\"{summary}\",\"category\":\"Bug\",\"severity\":\"High\",\"reasoning\":\"Crash on null.\"}}" }
                        }
                    }
                }
            }
        });

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent(GeminiOkJson()) };

    private static HttpResponseMessage StatusResponse(HttpStatusCode code, string body = "error") =>
        new(code) { Content = new StringContent(body) };

    // ──────────────────────────────────────────────
    // Test 1: Primary 200 → SUCCESS, fallback NOT called
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary200_ReturnsSuccess_FallbackNotCalled()
    {
        // Only one response queued — if fallback were called it would throw (queue empty)
        var handler = BuildHandler(MakeConfig(), Ok());

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.ResponsePayload.Should().Contain("summary");
    }

    // ──────────────────────────────────────────────
    // Test 2: Primary 503 → Fallback 200 → SUCCESS
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary503_Fallback200_ReturnsSuccess()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"UNAVAILABLE\"}"),
            Ok());

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeTrue();
        result.ResponsePayload.Should().Contain("summary");
    }

    // ──────────────────────────────────────────────
    // Test 3: Primary 429 → Fallback 200 → SUCCESS
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary429_Fallback200_ReturnsSuccess()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.TooManyRequests, "{\"error\":\"RATE_LIMITED\"}"),
            Ok());

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    // Test 4: Primary 500 → Fallback 200 → SUCCESS
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary500_Fallback200_ReturnsSuccess()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.InternalServerError),
            Ok());

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    // Test 5: Primary 503 → Fallback 503 → RETRYABLE
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary503_Fallback503_ReturnsRetryable()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"UNAVAILABLE\"}"),
            StatusResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"UNAVAILABLE\"}"));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("AI Provider failed with status ServiceUnavailable");
    }

    // ──────────────────────────────────────────────
    // Test 6: Primary 503 → Fallback 429 → RETRYABLE
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary503_Fallback429_ReturnsRetryable()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.ServiceUnavailable),
            StatusResponse(HttpStatusCode.TooManyRequests));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("AI Provider failed with status TooManyRequests");
    }

    // ──────────────────────────────────────────────
    // Test 7: Primary 401 → Permanent failure, fallback NOT called
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary401_PermanentFailure_FallbackNotCalled()
    {
        // Only one response queued — queue would throw if fallback were called
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.Unauthorized, "{\"error\":\"UNAUTHENTICATED\"}"));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse("401 is a permanent auth failure");
        result.ErrorMessage.Should().Contain("AI Provider failed with status Unauthorized");
    }

    // ──────────────────────────────────────────────
    // Test 8: Primary 403 → Permanent failure, fallback NOT called
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary403_PermanentFailure_FallbackNotCalled()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.Forbidden, "{\"error\":\"PERMISSION_DENIED\"}"));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse("403 is a permanent auth failure");
        result.ErrorMessage.Should().Contain("AI Provider failed with status Forbidden");
    }

    // ──────────────────────────────────────────────
    // Test 9: Primary 404 → Permanent failure, fallback NOT called
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Primary404_PermanentFailure_FallbackNotCalled()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.NotFound, "{\"error\":\"NOT_FOUND\"}"));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse("404 = model not found, permanent");
        result.ErrorMessage.Should().Contain("AI Provider failed with status NotFound");
    }

    // ──────────────────────────────────────────────
    // Test 10: URL contains model name but NOT the API key
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Request_UrlContainsModelName_AndNotApiKey()
    {
        string? capturedUrl = null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString();
            })
            .ReturnsAsync(Ok());

        var client = new HttpClient(handlerMock.Object);
        var handler = new AiTriageActionHandler(client, MakeConfig(), _loggerMock.Object);

        await handler.ExecuteAsync(_context);

        capturedUrl.Should().NotBeNull();
        capturedUrl.Should().Contain(PrimaryModelName, "URL must include the model name");
        capturedUrl.Should().NotContain(FakeApiKey, "API key must NOT appear in the URL");
        capturedUrl.Should().NotContain("key=", "key= query param must NOT be present");
    }

    // ──────────────────────────────────────────────
    // Test 11: x-goog-api-key header is set, key not in URL
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Request_UsesXGoogApiKeyHeader()
    {
        string? capturedHeaderValue = null;
        string? capturedUrl = null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                req.Headers.TryGetValues("x-goog-api-key", out var vals);
                capturedHeaderValue = vals?.FirstOrDefault();
                capturedUrl = req.RequestUri?.ToString();
            })
            .ReturnsAsync(Ok());

        var client = new HttpClient(handlerMock.Object);
        var handler = new AiTriageActionHandler(client, MakeConfig(), _loggerMock.Object);

        await handler.ExecuteAsync(_context);

        capturedHeaderValue.Should().Be(FakeApiKey, "API key must be in x-goog-api-key header");
        capturedUrl.Should().NotContain(FakeApiKey, "API key must NOT be in URL");
    }

    // ──────────────────────────────────────────────
    // Test 12: API key never appears in stored response payload
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Success_ResponsePayload_DoesNotContainApiKey()
    {
        var handler = BuildHandler(MakeConfig(), Ok());

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeTrue();
        result.ResponsePayload.Should().NotContain(FakeApiKey, "API key must never appear in stored payloads");
        result.RequestPayload.Should().NotContain(FakeApiKey, "API key must never appear in stored payloads");
        result.ErrorMessage.Should().NotContain(FakeApiKey);
    }

    // ──────────────────────────────────────────────
    // Test 12b: API key never appears in error messages
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Failure_ErrorMessage_DoesNotContainApiKey()
    {
        var handler = BuildHandler(MakeConfig(),
            StatusResponse(HttpStatusCode.Unauthorized, "bad credentials"));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(FakeApiKey);
        result.RequestPayload.Should().NotContain(FakeApiKey);
    }

    // ──────────────────────────────────────────────
    // Test 13: Missing API key → permanent failure, no HTTP call
    // ──────────────────────────────────────────────
    [Fact]
    public async Task MissingApiKey_ReturnsPermanentFailure_NoHttpCall()
    {
        // No responses queued — any HTTP call would throw
        var handler = BuildHandler(MakeConfig(apiKey: ""));

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Gemini API key is not configured");
    }

    // ──────────────────────────────────────────────
    // Test 14: Malformed Gemini response (non-JSON ai text)
    // ──────────────────────────────────────────────
    [Fact]
    public async Task MalformedGeminiResponse_ReturnsTransientFailure()
    {
        var malformedResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = "This is not JSON at all!!!" } }
                    }
                }
            }
        });

        var handler = BuildHandler(
            MakeConfig(),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(malformedResponse)
            });

        var result = await handler.ExecuteAsync(_context);

        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeTrue("malformed response should be retried");
        result.ErrorMessage.Should().Contain("parsing error");
    }
}
