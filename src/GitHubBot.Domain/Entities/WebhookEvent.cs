using GitHubBot.Domain.Enums;

namespace GitHubBot.Domain.Entities;

public class WebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryId { get; set; } // NOT NULL per architecture review
    public string DeliveryId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Action { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Pending;
    public string RawPayload { get; set; } = string.Empty;
    public string? ParsedData { get; set; }

    // Retry and claim recovery fields
    public int AttemptCount { get; set; } = 0;
    public int MaxAttempts { get; set; } = 6;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ConnectedRepository Repository { get; set; } = null!;
    public ICollection<ActionExecution> ActionExecutions { get; set; } = new List<ActionExecution>();
}
