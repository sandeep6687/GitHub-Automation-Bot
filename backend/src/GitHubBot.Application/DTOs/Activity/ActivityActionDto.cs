namespace GitHubBot.Application.DTOs.Activity;

public class ActivityActionDto
{
    public string ActionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public int? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponsePayload { get; set; }
}
