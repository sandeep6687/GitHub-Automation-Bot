using GitHubBot.Domain.Entities;

namespace GitHubBot.Domain.ValueObjects;

public class RuleEvaluationResult
{
    public Guid RuleId { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public int Priority { get; init; }
    public bool Matched { get; init; }
    public IReadOnlyList<RuleAction> Actions { get; init; } = Array.Empty<RuleAction>();

    public static RuleEvaluationResult NotMatched(Rule rule) => new()
    {
        RuleId = rule.Id,
        RuleName = rule.Name,
        Priority = rule.Priority,
        Matched = false,
        Actions = Array.Empty<RuleAction>()
    };

    public static RuleEvaluationResult Match(Rule rule) => new()
    {
        RuleId = rule.Id,
        RuleName = rule.Name,
        Priority = rule.Priority,
        Matched = true,
        Actions = rule.Actions.OrderBy(a => a.ExecutionOrder).ToList()
    };
}
