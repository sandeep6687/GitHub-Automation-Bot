using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GitHubBot.Infrastructure.ActionHandlers;

public class AiTriageActionHandler : IActionHandler
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiTriageActionHandler> _logger;

    public ActionType ActionType => ActionType.AiTriage;

    public AiTriageActionHandler(HttpClient httpClient, IConfiguration configuration, ILogger<AiTriageActionHandler> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(ActionType actionType) => actionType == ActionType.AiTriage;

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ActionResult.Failed("Gemini API key is not configured.", null, null, 0, isTransient: false);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var eventType = context.WebhookEvent.EventType;
            var rawJson = context.WebhookEvent.RawPayload;
            var jsonNode = JsonNode.Parse(rawJson);
            
            var title = jsonNode?["issue"]?["title"]?.ToString() ?? jsonNode?["pull_request"]?["title"]?.ToString() ?? "Unknown Title";
            var body = jsonNode?["issue"]?["body"]?.ToString() ?? jsonNode?["pull_request"]?["body"]?.ToString() ?? "No body provided.";
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
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = promptText } }
                    }
                },
                generationConfig = new { response_mime_type = "application/json" }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, cancellationToken);
            
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini API failed with status {StatusCode}. Response: {Response}", response.StatusCode, responseString);
                return ActionResult.Failed($"AI Provider failed with status {response.StatusCode}", null, responseString, (int)sw.ElapsedMilliseconds, isTransient: true);
            }

            var responseJson = JsonNode.Parse(responseString);
            var aiText = responseJson?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            
            if (string.IsNullOrWhiteSpace(aiText))
            {
                return ActionResult.Failed("AI returned an empty response.", null, responseString, (int)sw.ElapsedMilliseconds, isTransient: true);
            }

            // Validate that it parses as JSON so we know it's valid structured data
            var structuredData = JsonNode.Parse(aiText);

            sw.Stop();
            return ActionResult.Succeeded(
                null,
                aiText,
                (int)sw.ElapsedMilliseconds
            );
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI JSON response.");
            return ActionResult.Failed($"AI response parsing error: {ex.Message}", null, null, (int)sw.ElapsedMilliseconds, isTransient: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AiTriage action.");
            return ActionResult.Failed($"AI processing error: {ex.Message}", null, null, (int)sw.ElapsedMilliseconds, isTransient: true);
        }
    }
}
