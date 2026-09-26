using System.Diagnostics;
using System.Text.Json;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Helpers;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Logic;

namespace GitHubBot.Infrastructure.ActionHandlers;

public class SlackNotificationActionHandler : IActionHandler
{
    private readonly ISlackApiClient _slackApiClient;

    public ActionType ActionType => ActionType.SlackNotify;

    public bool CanHandle(ActionType actionType) =>
        actionType == ActionType.SlackNotify || actionType == ActionType.SlackNotification;

    public SlackNotificationActionHandler(ISlackApiClient slackApiClient)
    {
        _slackApiClient = slackApiClient ?? throw new ArgumentNullException(nameof(slackApiClient));
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        // 1. Validate configuration
        string? messageTemplate = null;
        try
        {
            using var doc = JsonDocument.Parse(context.RuleAction.Configuration);
            if (doc.RootElement.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
            {
                messageTemplate = msgProp.GetString();
            }
            else if (doc.RootElement.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                messageTemplate = textProp.GetString();
            }
        }
        catch (JsonException)
        {
            return ActionResult.Failed("Invalid JSON in Slack configuration.", null, null, 0, isTransient: false);
        }

        if (string.IsNullOrWhiteSpace(messageTemplate))
        {
            return ActionResult.Failed("Slack configuration must specify a non-empty 'message'.", null, null, 0, isTransient: false);
        }

        // 2. Parse event payload variables safely
        var payloadData = WebhookPayloadParser.Parse(context.WebhookEvent.RawPayload, context.WebhookEvent.EventType);

        // 3. Render supported message template variables
        var renderedMessage = SlackTemplateRenderer.Render(
            messageTemplate,
            payloadData,
            context.Repository,
            context.WebhookEvent,
            context.IssueOrPrNumber);

        var sanitizedRequest = JsonSerializer.Serialize(new { text = renderedMessage });
        var sw = Stopwatch.StartNew();

        try
        {
            // 4. Send Slack notification (server-side webhook URL is resolved internally)
            await _slackApiClient.SendMessageAsync(renderedMessage, cancellationToken);
            sw.Stop();

            var sanitizedResponse = JsonSerializer.Serialize(new { status = "ok" });
            return ActionResult.Succeeded(sanitizedRequest, sanitizedResponse, (int)sw.ElapsedMilliseconds);
        }
        catch (SlackApiException ex)
        {
            sw.Stop();
            return ActionResult.Failed(ex.Message, sanitizedRequest, null, (int)sw.ElapsedMilliseconds, isTransient: ex.IsTransient);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return ActionResult.Failed(ex.Message, sanitizedRequest, null, (int)sw.ElapsedMilliseconds, isTransient: true);
        }
    }
}
