using System.Text.Json;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Domain.Logic;

namespace GitHubBot.Application.Services;

/// <summary>
/// Event processor integrating the deterministic Rule Engine and ActionDispatcher into the event processing pipeline.
/// Evaluates rules against the event, and executes matched actions sequentially with action-level idempotency.
/// </summary>
public class RuleExecutionProcessor : IEventProcessor
{
    private readonly IRuleRepository _ruleRepository;
    private readonly RuleEngine _ruleEngine;
    private readonly IActionDispatcher _actionDispatcher;
    private readonly IConnectedRepositoryRepository _connectedRepoRepository;

    public RuleExecutionProcessor(
        IRuleRepository ruleRepository,
        RuleEngine ruleEngine,
        IActionDispatcher actionDispatcher,
        IConnectedRepositoryRepository connectedRepoRepository)
    {
        _ruleRepository = ruleRepository ?? throw new ArgumentNullException(nameof(ruleRepository));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _actionDispatcher = actionDispatcher ?? throw new ArgumentNullException(nameof(actionDispatcher));
        _connectedRepoRepository = connectedRepoRepository ?? throw new ArgumentNullException(nameof(connectedRepoRepository));
    }

    public async Task ProcessAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        if (webhookEvent == null)
            throw new ArgumentNullException(nameof(webhookEvent));

        // 1. Compute full event key (e.g. "issues.opened" or "push")
        var eventKey = string.IsNullOrEmpty(webhookEvent.Action)
            ? webhookEvent.EventType
            : $"{webhookEvent.EventType}.{webhookEvent.Action}";

        // 2. Load enabled rules scoped to the event's repository and event type
        var rules = await _ruleRepository.GetActiveRulesForEventAsync(
            webhookEvent.RepositoryId,
            eventKey,
            cancellationToken);

        // 3. Evaluate rules with deterministic RuleEngine (pure domain logic)
        var matchedResults = _ruleEngine.EvaluateRules(rules, webhookEvent);

        var matchedRuleCount = matchedResults.Count;
        var totalActionCount = matchedResults.Sum(r => r.Actions.Count);

        // 4. Store deterministic evaluation summary in ParsedData
        webhookEvent.ParsedData = JsonSerializer.Serialize(new
        {
            matchedRules = matchedRuleCount,
            matchedRuleIds = matchedResults.Select(r => r.RuleId).ToList(),
            actionCount = totalActionCount
        });

        if (totalActionCount == 0)
        {
            return;
        }

        // 5. Load authoritative repository context
        var repository = webhookEvent.Repository
            ?? await _connectedRepoRepository.GetByIdAsync(webhookEvent.RepositoryId, cancellationToken);

        if (repository == null)
        {
            throw new InvalidOperationException($"Connected repository {webhookEvent.RepositoryId} not found.");
        }

        // 6. Extract issue or PR number from payload
        var issueOrPrNumber = WebhookPayloadParser.ExtractIssueOrPrNumber(webhookEvent.RawPayload, webhookEvent.EventType);

        // 7. Dispatch matched actions in deterministic order
        foreach (var ruleResult in matchedResults)
        {
            foreach (var action in ruleResult.Actions)
            {
                var result = await _actionDispatcher.DispatchActionAsync(
                    webhookEvent,
                    action,
                    repository,
                    issueOrPrNumber,
                    cancellationToken);

                if (!result.Success)
                {
                    if (!result.IsTransient)
                    {
                        // Permanent failure (401, 403, 404, invalid configuration) -> exhaust attempts immediately
                        webhookEvent.AttemptCount = webhookEvent.MaxAttempts;
                        throw new ActionExecutionException(
                            result.ErrorMessage ?? $"Action {action.Id} failed permanently.",
                            isTransient: false);
                    }

                    // Transient failure -> trigger retry via existing retry infrastructure
                    throw new ActionExecutionException(
                        result.ErrorMessage ?? $"Action {action.Id} failed transiently.",
                        isTransient: true);
                }
            }
        }
    }
}
