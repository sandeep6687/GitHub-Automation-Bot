using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Infrastructure.ExternalServices;

public class GitHubTokenProvider : IGitHubTokenProvider
{
    private readonly IConnectedRepositoryRepository _repoRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITokenEncryptionService _encryptionService;

    public GitHubTokenProvider(
        IConnectedRepositoryRepository repoRepository,
        IUserRepository userRepository,
        ITokenEncryptionService encryptionService)
    {
        _repoRepository = repoRepository ?? throw new ArgumentNullException(nameof(repoRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
    }

    public async Task<string> GetTokenForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var repo = await _repoRepository.GetByIdAsync(repositoryId, cancellationToken);
        if (repo == null)
        {
            throw new InvalidOperationException($"Connected repository {repositoryId} not found.");
        }

        var githubAccount = await _userRepository.GetGithubAccountByUserIdAsync(repo.UserId, cancellationToken);
        if (githubAccount == null || string.IsNullOrWhiteSpace(githubAccount.EncryptedAccessToken))
        {
            throw new InvalidOperationException($"No GitHub account or access token found for user {repo.UserId}.");
        }

        return _encryptionService.Decrypt(githubAccount.EncryptedAccessToken);
    }
}
