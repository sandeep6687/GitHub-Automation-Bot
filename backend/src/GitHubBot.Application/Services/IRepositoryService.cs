using GitHubBot.Application.DTOs.Repository;

namespace GitHubBot.Application.Services;

public interface IRepositoryService
{
    Task<IReadOnlyList<AvailableRepoDto>> GetAvailableRepositoriesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectedRepoDto>> GetConnectedRepositoriesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ConnectedRepoDto> ConnectRepositoryAsync(Guid userId, long githubRepositoryId, CancellationToken cancellationToken = default);
    Task DisconnectRepositoryAsync(Guid userId, Guid repositoryId, CancellationToken cancellationToken = default);
    Task<ConnectedRepoDto> SyncGitHubAppInstallationAsync(Guid userId, Guid repositoryId, CancellationToken cancellationToken = default);
}
