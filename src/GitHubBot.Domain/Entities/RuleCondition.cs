using GitHubBot.Domain.Enums;

namespace GitHubBot.Domain.Entities;

public class RuleCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public ConditionType ConditionType { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;

    // Navigation properties
    public Rule Rule { get; set; } = null!;
}
