using GitHubBot.Domain.Enums;

namespace GitHubBot.Domain.Entities;

public class ActionExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WebhookEventId { get; set; }
    public Guid? RuleActionId { get; set; }
    public ActionType ActionType { get; set; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public int? DurationMs { get; set; }

    // Navigation properties
    public WebhookEvent WebhookEvent { get; set; } = null!;
    public RuleAction? RuleAction { get; set; }
}
