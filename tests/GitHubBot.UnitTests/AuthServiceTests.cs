using FluentAssertions;
using GitHubBot.Application.DTOs.Auth;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using Moq;

namespace GitHubBot.UnitTests;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IGitHubOAuthClient> _gitHubOAuthClientMock = new();
    private readonly Mock<ITokenEncryptionService> _tokenEncryptionServiceMock = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _authService = new AuthService(
            _userRepositoryMock.Object,
            _gitHubOAuthClientMock.Object,
            _tokenEncryptionServiceMock.Object);
    }

    [Fact]
    public void GenerateAuthorizationUrl_ShouldGenerateStateAndReturnUrl()
    {
        _gitHubOAuthClientMock
            .Setup(c => c.GetAuthorizationUrl(It.IsAny<string>()))
            .Returns<string>(state => $"https://github.com/login/oauth/authorize?state={state}");

        var url = _authService.GenerateAuthorizationUrl(out var state);

        state.Should().NotBeNullOrWhiteSpace();
        state.Length.Should().Be(64); // 32 bytes hex encoded
        url.Should().Contain(state);
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldThrow_WhenStateIsMissing()
    {
        var act = () => _authService.ProcessOAuthCallbackAsync("code123", "", "expected_state");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*state*");
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldThrow_WhenExpectedStateIsMissing()
    {
        var act = () => _authService.ProcessOAuthCallbackAsync("code123", "provided_state", "");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*expected*");
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldThrow_WhenStateDoesNotMatchExpectedState()
    {
        var act = () => _authService.ProcessOAuthCallbackAsync("code123", "state_A", "state_B");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CSRF*");
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldThrow_WhenTokenExchangeFails()
    {
        _gitHubOAuthClientMock
            .Setup(c => c.ExchangeCodeForTokenAsync("bad_code", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Bad verification code"));

        var act = () => _authService.ProcessOAuthCallbackAsync("bad_code", "valid_state", "valid_state");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bad verification code*");
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldThrow_WhenUserRetrievalFails()
    {
        _gitHubOAuthClientMock
            .Setup(c => c.ExchangeCodeForTokenAsync("good_code", It.IsAny<CancellationToken>()))
            .ReturnsAsync("gho_access_token_123");

        _gitHubOAuthClientMock
            .Setup(c => c.GetUserProfileAsync("gho_access_token_123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("GitHub API 500"));

        var act = () => _authService.ProcessOAuthCallbackAsync("good_code", "valid_state", "valid_state");
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldCreateNewUser_WhenUserDoesNotExist()
    {
        const string rawToken = "gho_new_user_token";
        const string encryptedToken = "encrypted_token_value";
        const string code = "valid_code";
        const string state = "valid_state";

        _gitHubOAuthClientMock
            .Setup(c => c.ExchangeCodeForTokenAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawToken);

        _gitHubOAuthClientMock
            .Setup(c => c.GetUserProfileAsync(rawToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubUserResponse
            {
                Id = 998877,
                Login = "newdeveloper",
                Email = "dev@example.com",
                AvatarUrl = "https://avatars.githubusercontent.com/u/998877"
            });

        _tokenEncryptionServiceMock
            .Setup(e => e.Encrypt(rawToken))
            .Returns(encryptedToken);

        _userRepositoryMock
            .Setup(r => r.FindByGithubUserIdAsync(998877, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        User? capturedUser = null;
        _userRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => capturedUser = u)
            .ReturnsAsync((User u, CancellationToken _) => u);

        var result = await _authService.ProcessOAuthCallbackAsync(code, state, state);

        result.Should().NotBeNull();
        result.Email.Should().Be("dev@example.com");
        result.Login.Should().Be("newdeveloper");
        result.GithubUserId.Should().Be(998877);

        capturedUser.Should().NotBeNull();
        capturedUser!.GithubAccount.Should().NotBeNull();
        capturedUser.GithubAccount!.GithubUserId.Should().Be(998877);
        capturedUser.GithubAccount.EncryptedAccessToken.Should().Be(encryptedToken);

        // Security check: raw access token is NOT in the DTO
        result.GetType().GetProperties().Select(p => p.Name).Should().NotContain("AccessToken");
        result.GetType().GetProperties().Select(p => p.Name).Should().NotContain("EncryptedAccessToken");
    }

    [Fact]
    public async Task ProcessOAuthCallback_ShouldLoginAndUpateExistingUser_WhenUserExists()
    {
        const string rawToken = "gho_existing_token";
        const string encryptedToken = "new_encrypted_token";
        const string code = "valid_code";
        const string state = "valid_state";

        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "old@example.com",
            GithubAccount = new GithubAccount
            {
                Id = Guid.NewGuid(),
                GithubUserId = 554433,
                Login = "oldlogin",
                EncryptedAccessToken = "old_encrypted_token"
            }
        };

        _gitHubOAuthClientMock
            .Setup(c => c.ExchangeCodeForTokenAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawToken);

        _gitHubOAuthClientMock
            .Setup(c => c.GetUserProfileAsync(rawToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubUserResponse
            {
                Id = 554433,
                Login = "updatedlogin",
                Email = "new@example.com",
                AvatarUrl = "https://avatars.githubusercontent.com/u/554433"
            });

        _tokenEncryptionServiceMock
            .Setup(e => e.Encrypt(rawToken))
            .Returns(encryptedToken);

        _userRepositoryMock
            .Setup(r => r.FindByGithubUserIdAsync(554433, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var result = await _authService.ProcessOAuthCallbackAsync(code, state, state);

        result.Id.Should().Be(existingUser.Id);
        result.Login.Should().Be("updatedlogin");
        result.Email.Should().Be("new@example.com");

        _userRepositoryMock.Verify(r => r.UpdateGithubAccountAsync(
            It.Is<GithubAccount>(a => a.EncryptedAccessToken == encryptedToken && a.Login == "updatedlogin"),
            It.IsAny<CancellationToken>()), Times.Once);

        _userRepositoryMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Email == "new@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
