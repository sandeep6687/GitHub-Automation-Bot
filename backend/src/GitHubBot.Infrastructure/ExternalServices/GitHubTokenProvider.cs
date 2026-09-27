using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Infrastructure.ExternalServices;

public class GitHubTokenProvider : IGitHubTokenProvider
{
    private readonly IConnectedRepositoryRepository _repoRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly IGitHubAppTokenProvider _appTokenProvider;

    public GitHubTokenProvider(
        IConnectedRepositoryRepository repoRepository,
        IUserRepository userRepository,
        ITokenEncryptionService encryptionService,
        IGitHubAppTokenProvider appTokenProvider)
    {
        _repoRepository = repoRepository ?? throw new ArgumentNullException(nameof(repoRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _appTokenProvider = appTokenProvider ?? throw new ArgumentNullException(nameof(appTokenProvider));
    }

    public async Task<string> GetTokenForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var repo = await _repoRepository.GetByIdAsync(repositoryId, cancellationToken);
        if (repo == null)
        {
            throw new InvalidOperationException($"Connected repository {repositoryId} not found.");
        }

        if (!repo.InstallationId.HasValue || repo.InstallationId.Value <= 0)
        {
            throw new GitHubBot.Domain.Exceptions.GitHubAppPermissionRequiredException(
                $"GitHub App permission is required to perform actions on repository {repo.Owner}/{repo.Name}."
            );
        }

        return await _appTokenProvider.GetInstallationTokenAsync(repo.InstallationId.Value, cancellationToken);
    }
}
