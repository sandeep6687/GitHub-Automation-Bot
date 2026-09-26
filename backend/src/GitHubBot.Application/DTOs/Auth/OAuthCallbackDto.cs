namespace GitHubBot.Application.DTOs.Auth;

public class OAuthCallbackDto
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
