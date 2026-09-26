namespace GitHubBot.Application.Configuration;

public class SlackOptions
{
    public const string SectionName = "Slack";
    public string? WebhookUrl { get; set; }
}
