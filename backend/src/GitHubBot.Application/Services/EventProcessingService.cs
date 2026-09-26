using GitHubBot.Application.DTOs.Worker;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Domain.Logic;

namespace GitHubBot.Application.Services;

public class EventProcessingService : IEventProcessingService
{
    private readonly IWebhookEventRepository _eventRepository;
    private readonly IEventProcessor _processor;

    public EventProcessingService(
        IWebhookEventRepository eventRepository,
        IEventProcessor processor)
    {
        _eventRepository = eventRepository;
        _processor = processor;
    }

    public async Task<EventProcessingResult?> ProcessEventByIdAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var evt = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (evt == null)
        {
            return null;
        }

        return await ProcessEventAsync(evt, cancellationToken);
    }

    public async Task<EventProcessingResult> ProcessEventAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Execute event processing lifecycle (zero external actions in Phase 5)
            await _processor.ProcessAsync(webhookEvent, cancellationToken);

            // Transition to SUCCESS
            webhookEvent.Status = EventStatus.Success;
            webhookEvent.ProcessedAt = DateTime.UtcNow;
            webhookEvent.LastError = null;
            webhookEvent.UpdatedAt = DateTime.UtcNow;

            await _eventRepository.UpdateAsync(webhookEvent, cancellationToken);

            return EventProcessingResult.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (RetryCalculator.CanRetry(webhookEvent.AttemptCount, webhookEvent.MaxAttempts))
            {
                var nextRetry = RetryCalculator.CalculateNextRetry(webhookEvent.AttemptCount, webhookEvent.MaxAttempts);
                webhookEvent.Status = EventStatus.Retrying;
                webhookEvent.NextRetryAt = nextRetry;
                webhookEvent.LastError = ex.Message;
                webhookEvent.UpdatedAt = DateTime.UtcNow;

                await _eventRepository.UpdateAsync(webhookEvent, cancellationToken);

                return EventProcessingResult.Retry(nextRetry, ex.Message);
            }
            else
            {
                webhookEvent.Status = EventStatus.Failed;
                webhookEvent.ProcessedAt = DateTime.UtcNow;
                webhookEvent.LastError = ex.Message;
                webhookEvent.UpdatedAt = DateTime.UtcNow;

                await _eventRepository.UpdateAsync(webhookEvent, cancellationToken);

                return EventProcessingResult.Exhausted(ex.Message);
            }
        }
    }
}
