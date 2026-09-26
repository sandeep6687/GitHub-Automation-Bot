using System.Text.Json;
using GitHubBot.Domain.ValueObjects;

namespace GitHubBot.Domain.Logic;

public static class WebhookPayloadParser
{
    public static WebhookPayloadData Parse(string? rawJson, string eventType)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new WebhookPayloadData();
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? action = null;
            if (root.TryGetProperty("action", out var actionProp) && actionProp.ValueKind == JsonValueKind.String)
            {
                action = actionProp.GetString();
            }

            string? title = null;
            string? author = null;
            var labels = new List<string>();
            int? issueOrPrNumber = null;
            string? htmlUrl = null;

            // 1. Issues event
            if (eventType.StartsWith("issues", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("issue", out var issue) && issue.ValueKind == JsonValueKind.Object)
                {
                    if (issue.TryGetProperty("number", out var numProp) && numProp.TryGetInt32(out var num))
                    {
                        issueOrPrNumber = num;
                    }

                    if (issue.TryGetProperty("html_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        htmlUrl = urlProp.GetString();
                    }

                    if (issue.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                    {
                        title = titleProp.GetString();
                    }

                    if (issue.TryGetProperty("user", out var userProp) &&
                        userProp.ValueKind == JsonValueKind.Object &&
                        userProp.TryGetProperty("login", out var loginProp) &&
                        loginProp.ValueKind == JsonValueKind.String)
                    {
                        author = loginProp.GetString();
                    }

                    if (issue.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var labelElem in labelsProp.EnumerateArray())
                        {
                            if (labelElem.ValueKind == JsonValueKind.Object &&
                                labelElem.TryGetProperty("name", out var nameProp) &&
                                nameProp.ValueKind == JsonValueKind.String)
                            {
                                var name = nameProp.GetString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    labels.Add(name);
                                }
                            }
                        }
                    }
                }
            }
            // 2. Pull Request event
            else if (eventType.StartsWith("pull_request", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("pull_request", out var pr) && pr.ValueKind == JsonValueKind.Object)
                {
                    if (pr.TryGetProperty("number", out var numProp) && numProp.TryGetInt32(out var num))
                    {
                        issueOrPrNumber = num;
                    }

                    if (pr.TryGetProperty("html_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        htmlUrl = urlProp.GetString();
                    }

                    if (pr.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                    {
                        title = titleProp.GetString();
                    }

                    if (pr.TryGetProperty("user", out var userProp) &&
                        userProp.ValueKind == JsonValueKind.Object &&
                        userProp.TryGetProperty("login", out var loginProp) &&
                        loginProp.ValueKind == JsonValueKind.String)
                    {
                        author = loginProp.GetString();
                    }

                    if (pr.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var labelElem in labelsProp.EnumerateArray())
                        {
                            if (labelElem.ValueKind == JsonValueKind.Object &&
                                labelElem.TryGetProperty("name", out var nameProp) &&
                                nameProp.ValueKind == JsonValueKind.String)
                            {
                                var name = nameProp.GetString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    labels.Add(name);
                                }
                            }
                        }
                    }
                }
            }

            return new WebhookPayloadData
            {
                Title = title,
                Author = author,
                Labels = labels,
                Action = action,
                IssueOrPrNumber = issueOrPrNumber,
                HtmlUrl = htmlUrl
            };
        }
        catch (JsonException)
        {
            // Malformed JSON returns empty data without crashing
            return new WebhookPayloadData();
        }
    }

    public static int? ExtractIssueOrPrNumber(string? rawJson, string eventType)
    {
        return Parse(rawJson, eventType).IssueOrPrNumber;
    }
}
