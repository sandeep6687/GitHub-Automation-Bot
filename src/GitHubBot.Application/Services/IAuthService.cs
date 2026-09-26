using GitHubBot.Application.DTOs.Auth;

namespace GitHubBot.Application.Services;

public interface IAuthService
{
    string GenerateAuthorizationUrl(out string state);
    Task<UserProfileDto> ProcessOAuthCallbackAsync(string code, string state, string expectedState, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}
