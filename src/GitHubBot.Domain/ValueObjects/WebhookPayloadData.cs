namespace GitHubBot.Domain.ValueObjects;

public class WebhookPayloadData
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public string? Action { get; init; }
}
