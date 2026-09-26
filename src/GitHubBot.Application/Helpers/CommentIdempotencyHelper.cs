namespace GitHubBot.Application.Helpers;

public static class CommentIdempotencyHelper
{
    public static string GenerateMarker(Guid webhookEventId, Guid ruleActionId)
    {
        return $"<!-- github-bot:event:{webhookEventId}:action:{ruleActionId} -->";
    }

    public static string AttachMarker(string body, Guid webhookEventId, Guid ruleActionId)
    {
        var marker = GenerateMarker(webhookEventId, ruleActionId);
        if (string.IsNullOrWhiteSpace(body))
        {
            return marker;
        }

        return $"{body.TrimEnd()}\n\n{marker}";
    }

    public static bool ContainsMarker(string? commentBody, Guid webhookEventId, Guid ruleActionId)
    {
        if (string.IsNullOrEmpty(commentBody))
        {
            return false;
        }

        var marker = GenerateMarker(webhookEventId, ruleActionId);
        return commentBody.Contains(marker, StringComparison.Ordinal);
    }
}
