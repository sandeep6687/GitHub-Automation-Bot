using GitHubBot.Domain.Entities;

namespace GitHubBot.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> FindByGithubUserIdAsync(long githubUserId, CancellationToken cancellationToken = default);
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task<GithubAccount?> GetGithubAccountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UpdateGithubAccountAsync(GithubAccount account, CancellationToken cancellationToken = default);
}
