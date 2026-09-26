using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Domain.Entities;

namespace GitHubBot.Application.Interfaces;

public interface IActionDispatcher
{
    Task<ActionExecutionResult> DispatchActionAsync(
        WebhookEvent webhookEvent,
        RuleAction ruleAction,
        ConnectedRepository repository,
        int? issueOrPrNumber,
        CancellationToken cancellationToken = default);
}
