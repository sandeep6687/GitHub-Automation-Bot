using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.ValueObjects;

namespace GitHubBot.Domain.Logic;

/// <summary>
/// Deterministic Rule Engine.
/// Pure domain logic: no I/O, no async, no side effects.
/// Evaluates webhook events against repository automation rules.
/// </summary>
public class RuleEngine
{
    public RuleEvaluationResult EvaluateRule(
        Rule rule,
        WebhookPayloadData payload,
        string eventType,
        string? action = null,
        Guid? targetRepositoryId = null)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (payload == null)
            payload = new WebhookPayloadData();

        // 1. Rule must be enabled
        if (!rule.IsEnabled)
        {
            return RuleEvaluationResult.NotMatched(rule);
        }

        // 2. Rule must belong to the target connected repository
        if (targetRepositoryId.HasValue && rule.RepositoryId != targetRepositoryId.Value)
        {
            return RuleEvaluationResult.NotMatched(rule);
        }

        // 3. Rule EventType must match event type and action
        var effectiveAction = !string.IsNullOrEmpty(action) ? action : payload.Action;
        if (!MatchesEventTypeAndAction(rule.EventType, eventType, effectiveAction))
        {
            return RuleEvaluationResult.NotMatched(rule);
        }

        // 4. Rule with zero conditions does NOT match
        if (rule.Conditions == null || rule.Conditions.Count == 0)
        {
            return RuleEvaluationResult.NotMatched(rule);
        }

        // 5. Evaluate all conditions using AND semantics
        foreach (var condition in rule.Conditions)
        {
            if (!EvaluateCondition(condition, payload))
            {
                return RuleEvaluationResult.NotMatched(rule);
            }
        }

        // 6. All conditions matched: return match with actions ordered by ExecutionOrder ASC
        return RuleEvaluationResult.Match(rule);
    }

    public IReadOnlyList<RuleEvaluationResult> EvaluateRules(
        IEnumerable<Rule> rules,
        WebhookPayloadData payload,
        string eventType,
        string? action = null,
        Guid? targetRepositoryId = null)
    {
        if (rules == null)
            return Array.Empty<RuleEvaluationResult>();

        var matchingResults = new List<RuleEvaluationResult>();

        foreach (var rule in rules)
        {
            var result = EvaluateRule(rule, payload, eventType, action, targetRepositoryId);
            if (result.Matched)
            {
                matchingResults.Add(result);
            }
        }

        // Return all matching rules ordered by Priority ASC
        return matchingResults.OrderBy(r => r.Priority).ToList();
    }

    public IReadOnlyList<RuleEvaluationResult> EvaluateRules(IEnumerable<Rule> rules, WebhookEvent webhookEvent)
    {
        if (webhookEvent == null)
            throw new ArgumentNullException(nameof(webhookEvent));

        var payload = WebhookPayloadParser.Parse(webhookEvent.RawPayload, webhookEvent.EventType);
        var effectiveAction = !string.IsNullOrEmpty(webhookEvent.Action) ? webhookEvent.Action : payload.Action;

        return EvaluateRules(rules, payload, webhookEvent.EventType, effectiveAction, webhookEvent.RepositoryId);
    }

    private static bool EvaluateCondition(RuleCondition condition, WebhookPayloadData payload)
    {
        var comparison = condition.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        switch (condition.ConditionType)
        {
            case ConditionType.TitleContains:
                if (string.IsNullOrEmpty(payload.Title) || condition.Value == null)
                    return false;
                return payload.Title.Contains(condition.Value, comparison);

            case ConditionType.AuthorEquals:
                if (string.IsNullOrEmpty(payload.Author) || condition.Value == null)
                    return false;
                return payload.Author.Equals(condition.Value, comparison);

            case ConditionType.LabelContains:
                if (payload.Labels == null || payload.Labels.Count == 0 || condition.Value == null)
                    return false;
                return payload.Labels.Any(label => label.Contains(condition.Value, comparison));

            default:
                return false;
        }
    }

    public static bool MatchesEventTypeAndAction(string ruleEventType, string eventType, string? action)
    {
        if (string.IsNullOrWhiteSpace(ruleEventType) || string.IsNullOrWhiteSpace(eventType))
            return false;

        // Normalize separators: accept "issues/opened" or "issues.opened"
        var normalizedRule = ruleEventType.Replace('/', '.').Trim();
        var normalizedEvent = eventType.Replace('/', '.').Trim();
        var fullIncoming = string.IsNullOrEmpty(action)
            ? normalizedEvent
            : $"{normalizedEvent}.{action.Trim()}";

        // Exact full event match (e.g. "issues.opened" == "issues.opened")
        if (normalizedRule.Equals(fullIncoming, StringComparison.OrdinalIgnoreCase))
            return true;

        // If rule specifies action (contains '.')
        if (normalizedRule.Contains('.'))
        {
            var parts = normalizedRule.Split('.', 2);
            return parts[0].Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase)
                && parts[1].Equals(action, StringComparison.OrdinalIgnoreCase);
        }

        // If rule specifies only event type without action (e.g. "push")
        // and incoming event has no action
        if (string.IsNullOrEmpty(action))
        {
            return normalizedRule.Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase);
        }

        // A rule targeting base "issues" does not match every action ("opened", "closed", "labeled")
        // unless configured for that action.
        return false;
    }
}
