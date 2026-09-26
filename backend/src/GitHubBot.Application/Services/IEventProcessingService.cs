using GitHubBot.Application.DTOs.Worker;
using GitHubBot.Domain.Entities;

namespace GitHubBot.Application.Services;

public interface IEventProcessingService
{
    Task<EventProcessingResult> ProcessEventAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
    Task<EventProcessingResult?> ProcessEventByIdAsync(Guid eventId, CancellationToken cancellationToken = default);
}
