using System.Text.RegularExpressions;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.ValueObjects;

namespace GitHubBot.Application.Helpers;

/// <summary>
/// Safe and deterministic template renderer for Slack messages.
/// Only supports an explicit, approved set of event variables.
/// Unknown variables remain unchanged (e.g. {{unknown}} stays {{unknown}}).
/// Zero code execution, zero reflection, zero security exposure.
/// </summary>
public static class SlackTemplateRenderer
{
    private static readonly Regex VariableRegex = new(@"\{\{([a-zA-Z0-9_]+)\}\}", RegexOptions.Compiled);

    public static string Render(
        string? template,
        WebhookPayloadData? payload,
        ConnectedRepository? repository,
        WebhookEvent? webhookEvent,
        int? issueOrPrNumber)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var payloadData = payload ?? new WebhookPayloadData();
        var repoFullName = repository?.FullName ?? string.Empty;
        var eventType = webhookEvent?.EventType ?? string.Empty;
        var action = webhookEvent?.Action ?? payloadData.Action ?? string.Empty;
        var issueNumStr = issueOrPrNumber?.ToString() ?? (payloadData.IssueOrPrNumber?.ToString() ?? string.Empty);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = payloadData.Title ?? string.Empty,
            ["author"] = payloadData.Author ?? string.Empty,
            ["action"] = action,
            ["repository"] = repoFullName,
            ["event"] = eventType,
            ["issueNumber"] = issueNumStr,
            ["url"] = payloadData.HtmlUrl ?? string.Empty
        };

        return VariableRegex.Replace(template, match =>
        {
            var varName = match.Groups[1].Value;
            if (variables.TryGetValue(varName, out var value))
            {
                return value;
            }

            // Unknown variables remain unchanged
            return match.Value;
        });
    }
}
