using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Infrastructure.ExternalServices;
using Moq;

namespace GitHubBot.UnitTests;

public class GitHubTokenProviderTests
{
    private readonly Mock<IConnectedRepositoryRepository> _repoRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<ITokenEncryptionService> _encryptionMock;
    private readonly Mock<IGitHubAppTokenProvider> _appTokenProviderMock;
    private readonly GitHubTokenProvider _provider;

    public GitHubTokenProviderTests()
    {
        _repoRepoMock = new Mock<IConnectedRepositoryRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _encryptionMock = new Mock<ITokenEncryptionService>();
        _appTokenProviderMock = new Mock<IGitHubAppTokenProvider>();

        _provider = new GitHubTokenProvider(
            _repoRepoMock.Object,
            _userRepoMock.Object,
            _encryptionMock.Object,
            _appTokenProviderMock.Object);
    }

    [Fact]
    public async Task GetTokenForRepositoryAsync_WithInstallationId_UsesAppTokenProvider()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        _repoRepoMock.Setup(x => x.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository { InstallationId = 12345 });

        _appTokenProviderMock.Setup(x => x.GetInstallationTokenAsync(12345, It.IsAny<CancellationToken>()))
            .ReturnsAsync("app_installation_token");

        // Act
        var token = await _provider.GetTokenForRepositoryAsync(repoId);

        // Assert
        Assert.Equal("app_installation_token", token);
        _userRepoMock.Verify(x => x.GetGithubAccountByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTokenForRepositoryAsync_WithoutInstallationId_ThrowsPermissionException()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repoRepoMock.Setup(x => x.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository { UserId = userId, InstallationId = null });

        // Act
        var act = () => _provider.GetTokenForRepositoryAsync(repoId);

        // Assert
        await Assert.ThrowsAsync<GitHubBot.Domain.Exceptions.GitHubAppPermissionRequiredException>(act);
        _appTokenProviderMock.Verify(x => x.GetInstallationTokenAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
