using System.Text.Json.Serialization;

namespace GitHubBot.Application.DTOs.GitHub;

public class GitHubCommentDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("user")]
    public GitHubCommentUserDto? User { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class GitHubCommentUserDto
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;
}
