using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;

namespace GitHubBot.Application.Services;

/// <summary>
/// Default event processor for Phase 5.
/// Demonstrates durable event delivery without executing external GitHub/Slack side effects.
/// In Phase 6 & 7, rule evaluation and action dispatch will be integrated here.
/// </summary>
public class DefaultEventProcessor : IEventProcessor
{
    public Task ProcessAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        // Zero external actions in Phase 5 per architectural constraints.
        return Task.CompletedTask;
    }
}
