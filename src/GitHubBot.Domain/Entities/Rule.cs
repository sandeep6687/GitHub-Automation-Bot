namespace GitHubBot.Domain.Entities;

public class Rule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ConnectedRepository Repository { get; set; } = null!;
    public ICollection<RuleCondition> Conditions { get; set; } = new List<RuleCondition>();
    public ICollection<RuleAction> Actions { get; set; } = new List<RuleAction>();
}
