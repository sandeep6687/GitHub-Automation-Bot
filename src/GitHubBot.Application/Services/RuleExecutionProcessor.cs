using System.Text.Json;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Domain.Logic;

namespace GitHubBot.Application.Services;

/// <summary>
/// Event processor integrating the deterministic Rule Engine into the event processing pipeline.
/// In Phase 6, this evaluates rules against the event and determines matching actions without executing them.
/// In Phase 7, ActionDispatcher will be invoked to execute the returned actions.
/// </summary>
public class RuleExecutionProcessor : IEventProcessor
{
    private readonly IRuleRepository _ruleRepository;
    private readonly RuleEngine _ruleEngine;

    public RuleExecutionProcessor(
        IRuleRepository ruleRepository,
        RuleEngine ruleEngine)
    {
        _ruleRepository = ruleRepository;
        _ruleEngine = ruleEngine;
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

        // 2. Evaluate rules with deterministic RuleEngine (pure domain logic)
        var matchedResults = _ruleEngine.EvaluateRules(rules, webhookEvent);

        var matchedRuleCount = matchedResults.Count;
        var totalActionCount = matchedResults.Sum(r => r.Actions.Count);

        // 3. Store deterministic evaluation summary in ParsedData
        webhookEvent.ParsedData = JsonSerializer.Serialize(new
        {
            matchedRules = matchedRuleCount,
            matchedRuleIds = matchedResults.Select(r => r.RuleId).ToList(),
            actionCount = totalActionCount
        });

        // 4. In Phase 6, do NOT execute actions.
        // In Phase 7, ActionDispatcher will be plugged in here to execute matched actions with idempotency.
    }
}
