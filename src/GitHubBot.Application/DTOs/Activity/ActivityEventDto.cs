namespace GitHubBot.Application.DTOs.Activity;

public class ActivityEventDto
{
    public Guid EventId { get; set; }
    public string DeliveryId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Action { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public List<ActivityActionDto> Actions { get; set; } = new();
}
