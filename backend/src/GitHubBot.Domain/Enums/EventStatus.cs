namespace GitHubBot.Domain.Enums;

public enum EventStatus
{
    Pending,
    Processing,
    Retrying,
    Success,
    Failed,
    Skipped
}
