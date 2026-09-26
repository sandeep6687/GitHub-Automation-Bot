using GitHubBot.Domain.Enums;

namespace GitHubBot.Domain.Entities;

public class RuleAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public ActionType ActionType { get; set; }
    public string Configuration { get; set; } = "{}";
    public int ExecutionOrder { get; set; } = 0;

    // Navigation properties
    public Rule Rule { get; set; } = null!;
    public ICollection<ActionExecution> ActionExecutions { get; set; } = new List<ActionExecution>();
}
