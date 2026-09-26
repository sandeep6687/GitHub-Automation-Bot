using GitHubBot.Application.DTOs.Auth;

namespace GitHubBot.Application.Interfaces;

public interface IGitHubOAuthClient
{
    string GetAuthorizationUrl(string state);
    Task<string> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default);
    Task<GitHubUserResponse> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken = default);
}
