using System.Security.Cryptography;
using System.Text;
using GitHubBot.Application.DTOs.Auth;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IGitHubOAuthClient _gitHubOAuthClient;
    private readonly ITokenEncryptionService _tokenEncryptionService;

    public AuthService(
        IUserRepository userRepository,
        IGitHubOAuthClient gitHubOAuthClient,
        ITokenEncryptionService tokenEncryptionService)
    {
        _userRepository = userRepository;
        _gitHubOAuthClient = gitHubOAuthClient;
        _tokenEncryptionService = tokenEncryptionService;
    }

    public string GenerateAuthorizationUrl(out string state)
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        state = Convert.ToHexString(bytes);
        return _gitHubOAuthClient.GetAuthorizationUrl(state);
    }

    public async Task<UserProfileDto> ProcessOAuthCallbackAsync(
        string code,
        string state,
        string expectedState,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("Missing OAuth state parameter.", nameof(state));

        if (string.IsNullOrWhiteSpace(expectedState))
            throw new ArgumentException("Missing expected OAuth state in session.", nameof(expectedState));

        var stateBytes = Encoding.UTF8.GetBytes(state);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedState);

        if (!CryptographicOperations.FixedTimeEquals(stateBytes, expectedBytes))
            throw new InvalidOperationException("Invalid OAuth state parameter. Possible CSRF attack detected.");

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Missing authorization code.", nameof(code));

        // Exchange code for token
        var accessToken = await _gitHubOAuthClient.ExchangeCodeForTokenAsync(code, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Failed to obtain access token from GitHub.");

        // Retrieve user profile from GitHub
        var ghUser = await _gitHubOAuthClient.GetUserProfileAsync(accessToken, cancellationToken);
        if (ghUser == null || ghUser.Id <= 0)
            throw new InvalidOperationException("Failed to retrieve user profile from GitHub.");

        // Encrypt the GitHub access token before storing
        var encryptedToken = _tokenEncryptionService.Encrypt(accessToken);

        // Find or create local user
        var existingUser = await _userRepository.FindByGithubUserIdAsync(ghUser.Id, cancellationToken);

        if (existingUser != null)
        {
            // Update existing user & GitHub account
            if (existingUser.GithubAccount != null)
            {
                existingUser.GithubAccount.Login = ghUser.Login;
                existingUser.GithubAccount.AvatarUrl = ghUser.AvatarUrl ?? string.Empty;
                existingUser.GithubAccount.EncryptedAccessToken = encryptedToken;
                existingUser.GithubAccount.UpdatedAt = DateTime.UtcNow;
                await _userRepository.UpdateGithubAccountAsync(existingUser.GithubAccount, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(ghUser.Email) && existingUser.Email != ghUser.Email)
            {
                existingUser.Email = ghUser.Email;
                await _userRepository.UpdateAsync(existingUser, cancellationToken);
            }

            return new UserProfileDto
            {
                Id = existingUser.Id,
                Email = existingUser.Email,
                Login = existingUser.GithubAccount?.Login ?? ghUser.Login,
                AvatarUrl = existingUser.GithubAccount?.AvatarUrl ?? ghUser.AvatarUrl ?? string.Empty,
                GithubUserId = ghUser.Id
            };
        }

        // Create new user
        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Email = ghUser.Email ?? $"{ghUser.Login}@users.noreply.github.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var newAccount = new GithubAccount
        {
            Id = Guid.NewGuid(),
            UserId = newUser.Id,
            GithubUserId = ghUser.Id,
            Login = ghUser.Login,
            AvatarUrl = ghUser.AvatarUrl ?? string.Empty,
            EncryptedAccessToken = encryptedToken,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        newUser.GithubAccount = newAccount;
        await _userRepository.CreateAsync(newUser, cancellationToken);

        return new UserProfileDto
        {
            Id = newUser.Id,
            Email = newUser.Email,
            Login = newAccount.Login,
            AvatarUrl = newAccount.AvatarUrl,
            GithubUserId = ghUser.Id
        };
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return null;

        return new UserProfileDto
        {
            Id = user.Id,
            Email = user.Email,
            Login = user.GithubAccount?.Login ?? string.Empty,
            AvatarUrl = user.GithubAccount?.AvatarUrl ?? string.Empty,
            GithubUserId = user.GithubAccount?.GithubUserId ?? 0
        };
    }
}
