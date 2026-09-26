using GitHubBot.Application.DTOs.Activity;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Application.Services;

public class ActivityService : IActivityService
{
    private readonly IConnectedRepositoryRepository _connectedRepositoryRepository;
    private readonly IWebhookEventRepository _webhookEventRepository;

    public ActivityService(
        IConnectedRepositoryRepository connectedRepositoryRepository,
        IWebhookEventRepository webhookEventRepository)
    {
        _connectedRepositoryRepository = connectedRepositoryRepository;
        _webhookEventRepository = webhookEventRepository;
    }

    public async Task<ActivityResponseDto> GetActivityAsync(
        Guid userId,
        Guid repositoryId,
        int limit = 50,
        DateTime? before = null,
        string? status = null,
        string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var repo = await _connectedRepositoryRepository.GetByIdAsync(repositoryId, cancellationToken);
        if (repo == null)
        {
            throw new KeyNotFoundException($"Repository with ID '{repositoryId}' was not found.");
        }

        if (repo.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot access activity for a repository you do not own.");
        }

        EventStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<EventStatus>(status.Trim(), true, out var es) && Enum.IsDefined(es))
            {
                parsedStatus = es;
            }
            else
            {
                throw new ArgumentException($"Invalid status filter: '{status}'. Valid statuses: Pending, Processing, Retrying, Success, Failed.");
            }
        }

        var clampedLimit = Math.Clamp(limit, 1, 100);

        var events = await _webhookEventRepository.GetRecentEventsAsync(
            repositoryId,
            clampedLimit,
            before,
            parsedStatus,
            string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
            cancellationToken);

        var items = events.Select(e => new ActivityEventDto
        {
            EventId = e.Id,
            DeliveryId = e.DeliveryId,
            EventType = e.EventType,
            Action = e.Action,
            Status = e.Status.ToString(),
            CreatedAt = e.CreatedAt,
            ProcessedAt = e.ProcessedAt,
            AttemptCount = e.AttemptCount,
            LastError = e.LastError,
            Actions = (e.ActionExecutions ?? Array.Empty<GitHubBot.Domain.Entities.ActionExecution>())
                .OrderBy(a => a.ExecutedAt)
                .Select(a => new ActivityActionDto
                {
                    ActionType = a.ActionType switch
                    {
                        ActionType.GithubAddLabel => "AddLabel",
                        ActionType.GithubAddComment => "AddComment",
                        _ => a.ActionType.ToString()
                    },
                    Status = a.Status.ToString(),
                    ExecutedAt = a.ExecutedAt,
                    DurationMs = a.DurationMs,
                    ErrorMessage = a.ErrorMessage
                }).ToList()
        }).ToList();

        return new ActivityResponseDto
        {
            Items = items
        };
    }
}
