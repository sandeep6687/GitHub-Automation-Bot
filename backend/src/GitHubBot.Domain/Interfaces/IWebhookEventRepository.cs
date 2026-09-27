using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;

namespace GitHubBot.Domain.Interfaces;

public interface IWebhookEventRepository
{
    Task<WebhookEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WebhookEvent?> GetByDeliveryIdAsync(string deliveryId, CancellationToken cancellationToken = default);
    Task<bool> TryInsertAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookEvent>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken = default);
    Task<int> RecoverStaleProcessingClaimsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default);
    Task UpdateAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid id, EventStatus status, string? parsedData = null, string? lastError = null, DateTime? nextRetryAt = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookEvent>> GetRecentEventsAsync(
        Guid repositoryId,
        int limit = 50,
        DateTime? before = null,
        EventStatus? status = null,
        string? eventType = null,
        CancellationToken cancellationToken = default);
}
