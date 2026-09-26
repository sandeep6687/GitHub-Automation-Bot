using GitHubBot.Domain.Entities;

namespace GitHubBot.Application.Interfaces;

public interface IEventProcessor
{
    Task ProcessAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
}
