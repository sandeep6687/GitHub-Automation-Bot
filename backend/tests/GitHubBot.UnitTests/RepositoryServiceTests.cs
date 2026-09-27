using FluentAssertions;
using GitHubBot.Application.DTOs.Repository;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using Moq;

namespace GitHubBot.UnitTests;

public class RepositoryServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IConnectedRepositoryRepository> _connectedRepoRepoMock = new();
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock = new();
    private readonly Mock<IGitHubAppTokenProvider> _gitHubAppTokenProviderMock = new();
    private readonly Mock<ITokenEncryptionService> _tokenEncryptionMock = new();
    private readonly RepositoryService _service;
    private const string WebhookUrl = "https://bot.example.com/api/webhooks/github";

    public RepositoryServiceTests()
    {
        _service = new RepositoryService(
            _userRepositoryMock.Object,
            _connectedRepoRepoMock.Object,
            _gitHubApiClientMock.Object,
            _gitHubAppTokenProviderMock.Object,
            _tokenEncryptionMock.Object,
            WebhookUrl);
    }

    [Fact]
    public async Task GetAvailableRepositories_ShouldReturnRepos_AndMarkConnected()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "test@example.com",
            GithubAccount = new GithubAccount
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EncryptedAccessToken = "enc_access_token"
            }
        };

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenEncryptionMock
            .Setup(e => e.Decrypt("enc_access_token"))
            .Returns("decrypted_token");

        var ghRepos = new List<AvailableRepoDto>
        {
            new() { Id = 101, FullName = "user/repo1", Name = "repo1", Owner = "user" },
            new() { Id = 102, FullName = "user/repo2", Name = "repo2", Owner = "user" }
        };

        _gitHubApiClientMock
            .Setup(c => c.GetUserRepositoriesAsync("decrypted_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ghRepos);

        var connectedRepos = new List<ConnectedRepository>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, GithubRepositoryId = 101, FullName = "user/repo1" }
        };

        _connectedRepoRepoMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectedRepos);

        var result = await _service.GetAvailableRepositoriesAsync(userId);

        result.Should().HaveCount(2);
        result.First(r => r.Id == 101).IsConnected.Should().BeTrue();
        result.First(r => r.Id == 102).IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectRepository_ShouldVerifyAccess_CreateWebhook_AndSaveEncryptedSecret()
    {
        var userId = Guid.NewGuid();
        const long ghRepoId = 12345;
        const string rawToken = "gho_user_token";
        const string encToken = "enc_user_token";

        var user = new User
        {
            Id = userId,
            GithubAccount = new GithubAccount
            {
                UserId = userId,
                EncryptedAccessToken = encToken
            }
        };

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenEncryptionMock
            .Setup(e => e.Decrypt(encToken))
            .Returns(rawToken);

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(ghRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectedRepository?)null);

        var repoDetails = new AvailableRepoDto
        {
            Id = ghRepoId,
            FullName = "octocat/hello-world",
            Name = "hello-world",
            Owner = "octocat",
            DefaultBranch = "main"
        };

        _gitHubApiClientMock
            .Setup(c => c.GetRepositoryByIdAsync(rawToken, ghRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoDetails);

        _gitHubAppTokenProviderMock
            .Setup(p => p.TryGetGitHubAppInstallationIdAsync("octocat", "hello-world", It.IsAny<CancellationToken>()))
            .ReturnsAsync(165544798L);

        string? capturedWebhookSecret = null;
        _gitHubApiClientMock
            .Setup(c => c.CreateWebhookAsync(
                rawToken,
                "octocat",
                "hello-world",
                WebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((_, _, _, _, secret, _) => capturedWebhookSecret = secret)
            .ReturnsAsync(998811L);

        _tokenEncryptionMock
            .Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns<string>(s => $"enc_{s}");

        ConnectedRepository? savedEntity = null;
        _connectedRepoRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ConnectedRepository>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectedRepository, CancellationToken>((r, _) => savedEntity = r)
            .ReturnsAsync((ConnectedRepository r, CancellationToken _) => r);

        var result = await _service.ConnectRepositoryAsync(userId, ghRepoId);

        result.Should().NotBeNull();
        result.GithubRepositoryId.Should().Be(ghRepoId);
        result.FullName.Should().Be("octocat/hello-world");

        // Verify webhook secret was created, sent to GitHub, and encrypted in DB
        capturedWebhookSecret.Should().NotBeNullOrWhiteSpace();
        capturedWebhookSecret!.Length.Should().Be(64); // 32 bytes hex encoded

        savedEntity.Should().NotBeNull();
        savedEntity!.WebhookId.Should().Be(998811L);
        savedEntity.EncryptedWebhookSecret.Should().Be($"enc_{capturedWebhookSecret}");
        savedEntity.InstallationId.Should().Be(165544798L);

        // Security check: raw webhook secret or encrypted secret is not exposed in DTO
        result.GetType().GetProperties().Select(p => p.Name).Should().NotContain("EncryptedWebhookSecret");
        result.GetType().GetProperties().Select(p => p.Name).Should().NotContain("WebhookSecret");
    }

    [Fact]
    public async Task Connect_WhenAppNotInstalled_ReturnsPermissionError_AndDoesNotPersist()
    {
        var userId = Guid.NewGuid();
        const long ghRepoId = 12345;
        const string rawToken = "gho_user_token";
        const string encToken = "enc_user_token";

        var user = new User { Id = userId, GithubAccount = new GithubAccount { UserId = userId, EncryptedAccessToken = encToken } };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokenEncryptionMock.Setup(e => e.Decrypt(encToken)).Returns(rawToken);

        _gitHubApiClientMock.Setup(c => c.GetRepositoryByIdAsync(rawToken, ghRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailableRepoDto { Id = ghRepoId, FullName = "user/repo", Owner = "user", Name = "repo" });

        // Simulate 404 (or 403) resulting in null
        _gitHubAppTokenProviderMock.Setup(p => p.TryGetGitHubAppInstallationIdAsync("user", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var act = () => _service.ConnectRepositoryAsync(userId, ghRepoId);

        await act.Should().ThrowAsync<GitHubBot.Domain.Exceptions.GitHubAppPermissionRequiredException>()
            .WithMessage("*GitHub App does not have permission*");

        _gitHubApiClientMock.Verify(c => c.CreateWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _connectedRepoRepoMock.Verify(r => r.AddAsync(It.IsAny<ConnectedRepository>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Connect_WhenWebhookCreationFails_CleansUp_AndAborts()
    {
        var userId = Guid.NewGuid();
        const long ghRepoId = 12345;
        const string encToken = "enc_user_token";

        var user = new User { Id = userId, GithubAccount = new GithubAccount { UserId = userId, EncryptedAccessToken = encToken } };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokenEncryptionMock.Setup(e => e.Decrypt(encToken)).Returns("raw_token");

        _gitHubApiClientMock.Setup(c => c.GetRepositoryByIdAsync("raw_token", ghRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailableRepoDto { Id = ghRepoId, Owner = "u", Name = "r" });

        _gitHubAppTokenProviderMock.Setup(p => p.TryGetGitHubAppInstallationIdAsync("u", "r", It.IsAny<CancellationToken>()))
            .ReturnsAsync(123456L);

        _gitHubApiClientMock.Setup(c => c.CreateWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("GitHub API down"));

        var act = () => _service.ConnectRepositoryAsync(userId, ghRepoId);

        await act.Should().ThrowAsync<Exception>().WithMessage("Failed to create webhook. Repository connection aborted.");
        _connectedRepoRepoMock.Verify(r => r.AddAsync(It.IsAny<ConnectedRepository>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Connect_WhenPersistenceFails_AfterWebhookCreation_DeletesWebhook()
    {
        var userId = Guid.NewGuid();
        const long ghRepoId = 12345;
        const string encToken = "enc_user_token";
        const string rawToken = "raw_token";
        const long webhookId = 998811L;

        var user = new User { Id = userId, GithubAccount = new GithubAccount { UserId = userId, EncryptedAccessToken = encToken } };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokenEncryptionMock.Setup(e => e.Decrypt(encToken)).Returns(rawToken);

        _gitHubApiClientMock.Setup(c => c.GetRepositoryByIdAsync(rawToken, ghRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailableRepoDto { Id = ghRepoId, Owner = "u", Name = "r" });

        _gitHubAppTokenProviderMock.Setup(p => p.TryGetGitHubAppInstallationIdAsync("u", "r", It.IsAny<CancellationToken>()))
            .ReturnsAsync(123456L);

        _gitHubApiClientMock.Setup(c => c.CreateWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhookId);

        _connectedRepoRepoMock.Setup(r => r.AddAsync(It.IsAny<ConnectedRepository>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database down"));

        var act = () => _service.ConnectRepositoryAsync(userId, ghRepoId);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Failed to persist repository connection. GitHub webhook was cleaned up.");

        _gitHubApiClientMock.Verify(c => c.DeleteWebhookAsync(rawToken, "u", "r", webhookId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectRepository_ShouldThrow_WhenRepositoryAlreadyConnected()
    {
        var userId = Guid.NewGuid();
        const long ghRepoId = 12345;

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, GithubAccount = new GithubAccount { EncryptedAccessToken = "tok" } });

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(ghRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository { GithubRepositoryId = ghRepoId });

        var act = () => _service.ConnectRepositoryAsync(userId, ghRepoId);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already connected*");
    }

    [Fact]
    public async Task DisconnectRepository_ShouldThrow_WhenUserDoesNotOwnRepository()
    {
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var repoId = Guid.NewGuid();

        _connectedRepoRepoMock
            .Setup(r => r.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository { Id = repoId, UserId = ownerId });

        var act = () => _service.DisconnectRepositoryAsync(attackerId, repoId);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*not own*");
    }

    [Fact]
    public async Task DisconnectRepository_ShouldDeleteWebhookAndDbRecord_WhenOwnedByUser()
    {
        var userId = Guid.NewGuid();
        var repoId = Guid.NewGuid();

        var repo = new ConnectedRepository
        {
            Id = repoId,
            UserId = userId,
            Owner = "octocat",
            Name = "repo",
            WebhookId = 554433
        };

        var user = new User
        {
            Id = userId,
            GithubAccount = new GithubAccount { EncryptedAccessToken = "enc_tok" }
        };

        _connectedRepoRepoMock
            .Setup(r => r.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenEncryptionMock
            .Setup(e => e.Decrypt("enc_tok"))
            .Returns("raw_tok");

        await _service.DisconnectRepositoryAsync(userId, repoId);

        _gitHubApiClientMock.Verify(c => c.DeleteWebhookAsync(
            "raw_tok",
            "octocat",
            "repo",
            554433,
            It.IsAny<CancellationToken>()), Times.Once);

        _connectedRepoRepoMock.Verify(r => r.DeleteAsync(repoId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
