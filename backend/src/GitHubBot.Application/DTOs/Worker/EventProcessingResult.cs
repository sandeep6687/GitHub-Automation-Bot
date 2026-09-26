using GitHubBot.Domain.Enums;

namespace GitHubBot.Application.DTOs.Worker;

public class EventProcessingResult
{
    public EventStatus FinalStatus { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? NextRetryAt { get; init; }
    public bool Retrying => FinalStatus == EventStatus.Retrying;
    public bool Succeeded => FinalStatus == EventStatus.Success;
    public bool Failed => FinalStatus == EventStatus.Failed;

    public static EventProcessingResult Success() => new()
    {
        FinalStatus = EventStatus.Success
    };

    public static EventProcessingResult Retry(DateTime? nextRetryAt, string? errorMessage) => new()
    {
        FinalStatus = EventStatus.Retrying,
        NextRetryAt = nextRetryAt,
        ErrorMessage = errorMessage
    };

    public static EventProcessingResult Exhausted(string? errorMessage) => new()
    {
        FinalStatus = EventStatus.Failed,
        ErrorMessage = errorMessage
    };
}
