using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;

namespace GitHubBot.Domain.Interfaces;

public interface IActionExecutionRepository
{
    Task<ActionExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ActionExecution?> GetByEventAndActionAsync(Guid webhookEventId, Guid ruleActionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActionExecution>> GetByWebhookEventIdAsync(Guid webhookEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActionExecution>> GetSuccessfulExecutionsAsync(Guid webhookEventId, CancellationToken cancellationToken = default);
    Task AddAsync(ActionExecution execution, CancellationToken cancellationToken = default);
    Task UpdateAsync(ActionExecution execution, CancellationToken cancellationToken = default);
}
