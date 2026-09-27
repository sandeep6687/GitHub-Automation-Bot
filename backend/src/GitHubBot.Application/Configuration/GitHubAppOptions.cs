namespace GitHubBot.Application.Configuration;

public class GitHubAppOptions
{
    public const string SectionName = "GitHubApp";

    public long? AppId { get; set; }
    public string? PrivateKey { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
