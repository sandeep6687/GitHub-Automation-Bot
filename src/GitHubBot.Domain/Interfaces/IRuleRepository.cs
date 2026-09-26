using GitHubBot.Domain.Entities;

namespace GitHubBot.Domain.Interfaces;

public interface IRuleRepository
{
    Task<Rule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Rule>> GetByRepositoryIdAsync(Guid repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Rule>> GetActiveRulesForEventAsync(Guid repositoryId, string eventType, CancellationToken cancellationToken = default);
    Task<Rule> AddAsync(Rule rule, CancellationToken cancellationToken = default);
    Task UpdateAsync(Rule rule, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
