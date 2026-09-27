using System.Security.Cryptography;
using GitHubBot.Application.DTOs.Repository;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Application.Services;

public class RepositoryService : IRepositoryService
{
    private readonly IUserRepository _userRepository;
    private readonly IConnectedRepositoryRepository _connectedRepoRepository;
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly ITokenEncryptionService _tokenEncryptionService;
    private readonly string _webhookCallbackUrl;

    public RepositoryService(
        IUserRepository userRepository,
        IConnectedRepositoryRepository connectedRepoRepository,
        IGitHubApiClient gitHubApiClient,
        ITokenEncryptionService tokenEncryptionService,
        string webhookCallbackUrl = "http://localhost:5000/api/webhooks/github")
    {
        _userRepository = userRepository;
        _connectedRepoRepository = connectedRepoRepository;
        _gitHubApiClient = gitHubApiClient;
        _tokenEncryptionService = tokenEncryptionService;
        _webhookCallbackUrl = webhookCallbackUrl;
    }

    public async Task<IReadOnlyList<AvailableRepoDto>> GetAvailableRepositoriesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.GithubAccount == null || string.IsNullOrWhiteSpace(user.GithubAccount.EncryptedAccessToken))
        {
            throw new InvalidOperationException("User has no connected GitHub account.");
        }

        var accessToken = _tokenEncryptionService.Decrypt(user.GithubAccount.EncryptedAccessToken);
        var availableRepos = await _gitHubApiClient.GetUserRepositoriesAsync(accessToken, cancellationToken);

        var connectedRepos = await _connectedRepoRepository.GetByUserIdAsync(userId, cancellationToken);
        var connectedRepoIds = new HashSet<long>(connectedRepos.Select(r => r.GithubRepositoryId));

        foreach (var repo in availableRepos)
        {
            repo.IsConnected = connectedRepoIds.Contains(repo.Id);
        }

        return availableRepos;
    }

    public async Task<IReadOnlyList<ConnectedRepoDto>> GetConnectedRepositoriesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var repos = await _connectedRepoRepository.GetByUserIdAsync(userId, cancellationToken);

        return repos.Select(r => new ConnectedRepoDto
        {
            Id = r.Id,
            GithubRepositoryId = r.GithubRepositoryId,
            FullName = r.FullName,
            Owner = r.Owner,
            Name = r.Name,
            DefaultBranch = r.DefaultBranch,
            InstallationId = r.InstallationId,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task<ConnectedRepoDto> ConnectRepositoryAsync(
        Guid userId,
        long githubRepositoryId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.GithubAccount == null || string.IsNullOrWhiteSpace(user.GithubAccount.EncryptedAccessToken))
        {
            throw new InvalidOperationException("User has no connected GitHub account.");
        }

        var existingConnection = await _connectedRepoRepository.FindByGithubRepositoryIdAsync(githubRepositoryId, cancellationToken);
        if (existingConnection != null)
        {
            throw new InvalidOperationException("Repository is already connected.");
        }

        var accessToken = _tokenEncryptionService.Decrypt(user.GithubAccount.EncryptedAccessToken);

        // 1. Verify repository ownership/access on GitHub
        var repoDetails = await _gitHubApiClient.GetRepositoryByIdAsync(accessToken, githubRepositoryId, cancellationToken);
        if (repoDetails == null)
        {
            throw new InvalidOperationException("Repository not found on GitHub or access was denied.");
        }

        // 1b. Check if GitHub App is installed
        var installationId = await _gitHubApiClient.GetAppInstallationIdForRepositoryAsync(
            accessToken, repoDetails.Owner, repoDetails.Name, cancellationToken);

        // 2. Generate cryptographically secure random per-repository webhook secret
        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        var webhookSecret = Convert.ToHexString(secretBytes);

        // 3. Encrypt webhook secret for storage
        var encryptedWebhookSecret = _tokenEncryptionService.Encrypt(webhookSecret);

        // 4. Create GitHub webhook
        long? webhookId = null;
        try
        {
            webhookId = await _gitHubApiClient.CreateWebhookAsync(
                accessToken,
                repoDetails.Owner,
                repoDetails.Name,
                _webhookCallbackUrl,
                webhookSecret,
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("reachable over the public Internet") || _webhookCallbackUrl.Contains("localhost"))
        {
            // When running locally without a public tunnel (e.g. ngrok/smee), GitHub rejects localhost webhook URLs.
            // Proceed with connecting the repository locally so rule configuration and local events work.
            webhookId = null;
        }

        // 5. Persist ConnectedRepository
        var connectedRepo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GithubRepositoryId = repoDetails.Id,
            FullName = repoDetails.FullName,
            Owner = repoDetails.Owner,
            Name = repoDetails.Name,
            DefaultBranch = repoDetails.DefaultBranch,
            WebhookId = webhookId,
            EncryptedWebhookSecret = encryptedWebhookSecret,
            InstallationId = installationId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var saved = await _connectedRepoRepository.AddAsync(connectedRepo, cancellationToken);

        return new ConnectedRepoDto
        {
            Id = saved.Id,
            GithubRepositoryId = saved.GithubRepositoryId,
            FullName = saved.FullName,
            Owner = saved.Owner,
            Name = saved.Name,
            DefaultBranch = saved.DefaultBranch,
            InstallationId = saved.InstallationId,
            IsActive = saved.IsActive,
            CreatedAt = saved.CreatedAt
        };
    }

    public async Task DisconnectRepositoryAsync(
        Guid userId,
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _connectedRepoRepository.GetByIdAsync(repositoryId, cancellationToken);
        if (repo == null)
            return;

        if (repo.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot disconnect a repository you do not own.");
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.GithubAccount != null && !string.IsNullOrWhiteSpace(user.GithubAccount.EncryptedAccessToken) && repo.WebhookId.HasValue)
        {
            try
            {
                var accessToken = _tokenEncryptionService.Decrypt(user.GithubAccount.EncryptedAccessToken);
                await _gitHubApiClient.DeleteWebhookAsync(accessToken, repo.Owner, repo.Name, repo.WebhookId.Value, cancellationToken);
            }
            catch
            {
                // Non-fatal: if webhook was already removed on GitHub, proceed with local deletion
            }
        }

        await _connectedRepoRepository.DeleteAsync(repositoryId, cancellationToken);
    }
}
