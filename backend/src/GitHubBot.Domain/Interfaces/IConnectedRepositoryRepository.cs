using GitHubBot.Domain.Entities;

namespace GitHubBot.Domain.Interfaces;

public interface IConnectedRepositoryRepository
{
    Task<ConnectedRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConnectedRepository?> FindByFullNameAsync(string fullName, CancellationToken cancellationToken = default);
    Task<ConnectedRepository?> FindByGithubRepositoryIdAsync(long githubRepositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectedRepository>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ConnectedRepository> AddAsync(ConnectedRepository repository, CancellationToken cancellationToken = default);
    Task UpdateAsync(ConnectedRepository repository, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
