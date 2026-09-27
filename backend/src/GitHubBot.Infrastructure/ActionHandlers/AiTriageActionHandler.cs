using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GitHubBot.Infrastructure.ActionHandlers;

/// <summary>
/// Calls Gemini to triage a GitHub issue/PR.
///
/// Retry semantics (max 2 Gemini HTTP calls per execution):
///   - Primary model called first.
///   - If primary returns 429/500/502/503/504 → try fallback model exactly once.
///   - If primary returns 400/401/403/404/422   → permanent failure, NO fallback.
///   - If fallback also returns a transient error → surface as RETRYABLE so the
///     existing DB-backed worker retry mechanism handles the next attempt.
///
/// Security:
///   - API key sent via x-goog-api-key header, never in the URL.
///   - API key never logged or stored in any ActionResult field.
/// </summary>
public class AiTriageActionHandler : IActionHandler
{
    // Status codes that are considered transient → fallback is attempted
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests,       // 429
        HttpStatusCode.InternalServerError,   // 500
        HttpStatusCode.BadGateway,            // 502
        HttpStatusCode.ServiceUnavailable,    // 503
        HttpStatusCode.GatewayTimeout,        // 504
    };

    private enum GeminiCallOutcome { Success, TransientFailure, PermanentFailure }

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiTriageActionHandler> _logger;

    public ActionType ActionType => ActionType.AiTriage;

    public AiTriageActionHandler(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AiTriageActionHandler> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(ActionType actionType) => actionType == ActionType.AiTriage;

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        // --- 1. Read and validate configuration (key never logged) ---
        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return ActionResult.Failed("Gemini API key is not configured.", null, null, 0, isTransient: false);

        var primaryModel = _configuration["Gemini:PrimaryModel"];
        if (string.IsNullOrWhiteSpace(primaryModel))
            return ActionResult.Failed("Gemini primary model is not configured.", null, null, 0, isTransient: false);

        var fallbackModel = _configuration["Gemini:FallbackModel"];
        if (string.IsNullOrWhiteSpace(fallbackModel))
            return ActionResult.Failed("Gemini fallback model is not configured.", null, null, 0, isTransient: false);

        var sw = Stopwatch.StartNew();

        try
        {
            // --- 2. Build prompt ---
            var eventType = context.WebhookEvent.EventType;
            var jsonNode = JsonNode.Parse(context.WebhookEvent.RawPayload);

            var title = jsonNode?["issue"]?["title"]?.ToString()
                     ?? jsonNode?["pull_request"]?["title"]?.ToString()
                     ?? "Unknown Title";
            var body = jsonNode?["issue"]?["body"]?.ToString()
                    ?? jsonNode?["pull_request"]?["body"]?.ToString()
                    ?? "No body provided.";
            var author = jsonNode?["sender"]?["login"]?.ToString() ?? "Unknown";

            var promptText = $@"
Please triage the following GitHub {eventType}:
Title: {title}
Author: {author}
Body: {body}

Respond with a JSON object containing the following keys:
- summary: A short summary of the issue/PR (max 2 sentences).
- category: One of [Bug, Feature, Documentation, Question, Other].
- severity: One of [Low, Medium, High, Critical].
- reasoning: Brief reasoning for the category and severity.
";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = promptText } } } },
                generationConfig = new { response_mime_type = "application/json" }
            };

            // --- 3. Try primary model (call #1) ---
            _logger.LogInformation(
                "AiTriage calling primary model {Model} for WebhookEvent {EventId}",
                primaryModel, context.WebhookEvent.Id);

            var (primaryOutcome, primaryResult) = await TryCallGeminiAsync(
                apiKey, primaryModel, requestBody, sw, cancellationToken);

            if (primaryOutcome == GeminiCallOutcome.Success)
                return primaryResult!;

            if (primaryOutcome == GeminiCallOutcome.PermanentFailure)
            {
                _logger.LogWarning(
                    "AiTriage primary model {Model} returned a permanent error for WebhookEvent {EventId}. Not attempting fallback.",
                    primaryModel, context.WebhookEvent.Id);
                return primaryResult!;
            }

            // --- 4. Try fallback model (call #2, only on transient primary failure) ---
            _logger.LogWarning(
                "AiTriage primary model {PrimaryModel} returned transient error for WebhookEvent {EventId}. Attempting fallback model {FallbackModel}.",
                primaryModel, context.WebhookEvent.Id, fallbackModel);

            var (fallbackOutcome, fallbackResult) = await TryCallGeminiAsync(
                apiKey, fallbackModel, requestBody, sw, cancellationToken);

            if (fallbackOutcome == GeminiCallOutcome.Success)
                return fallbackResult!;

            // Fallback also failed — return whatever the fallback produced (transient or permanent)
            _logger.LogWarning(
                "AiTriage fallback model {FallbackModel} also failed for WebhookEvent {EventId}. Returning retryable failure.",
                fallbackModel, context.WebhookEvent.Id);

            return fallbackResult!;
        }
        catch (JsonException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "AiTriage failed to parse Gemini JSON response for WebhookEvent {EventId}.", context.WebhookEvent.Id);
            return ActionResult.Failed($"AI response parsing error: {ex.Message}", null, null, (int)sw.ElapsedMilliseconds, isTransient: true);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogError(ex, "AiTriage unexpected error for WebhookEvent {EventId}.", context.WebhookEvent.Id);
            return ActionResult.Failed($"AI processing error: {ex.Message}", null, null, (int)sw.ElapsedMilliseconds, isTransient: true);
        }
    }

    /// <summary>
    /// Makes exactly one Gemini HTTP call.
    /// Returns (Success, result)          on 2xx.
    /// Returns (TransientFailure, result) on 429/5xx — caller may try fallback.
    /// Returns (PermanentFailure, result) on 4xx — caller must NOT try fallback.
    ///
    /// SECURITY: API key placed in x-goog-api-key header ONLY. Never in the URL.
    /// </summary>
    private async Task<(GeminiCallOutcome outcome, ActionResult? result)> TryCallGeminiAsync(
        string apiKey,
        string model,
        object requestBody,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        // Key goes in header — never in the URL
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(requestBody);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "AiTriage HTTP send failed for model {Model}.", model);
            return (GeminiCallOutcome.TransientFailure,
                ActionResult.Failed($"AI processing error: {ex.Message}", null, null, (int)sw.ElapsedMilliseconds, isTransient: true));
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            bool isTransient = TransientStatusCodes.Contains(response.StatusCode);
            var outcome = isTransient ? GeminiCallOutcome.TransientFailure : GeminiCallOutcome.PermanentFailure;

            _logger.LogWarning(
                "AiTriage model {Model} returned {StatusCode} (transient={IsTransient}).",
                model, (int)response.StatusCode, isTransient);

            return (outcome, ActionResult.Failed(
                $"AI Provider failed with status {response.StatusCode}",
                null,
                responseString,
                (int)sw.ElapsedMilliseconds,
                isTransient: isTransient));
        }

        // 2xx — extract the text from candidates[0].content.parts[0].text
        var responseJson = JsonNode.Parse(responseString);
        var aiText = responseJson?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

        if (string.IsNullOrWhiteSpace(aiText))
        {
            _logger.LogWarning("AiTriage model {Model} returned 2xx but with empty content.", model);
            return (GeminiCallOutcome.TransientFailure,
                ActionResult.Failed("AI returned an empty response.", null, responseString, (int)sw.ElapsedMilliseconds, isTransient: true));
        }

        // Validate the AI text is itself valid JSON (response_mime_type enforces this, but we double-check)
        JsonNode.Parse(aiText); // throws JsonException → caught by caller

        sw.Stop();
        _logger.LogInformation("AiTriage model {Model} succeeded.", model);

        return (GeminiCallOutcome.Success, ActionResult.Succeeded(null, aiText, (int)sw.ElapsedMilliseconds));
    }
}
