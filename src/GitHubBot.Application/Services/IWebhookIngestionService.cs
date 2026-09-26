using GitHubBot.Application.DTOs.Webhook;

namespace GitHubBot.Application.Services;

public interface IWebhookIngestionService
{
    Task<WebhookIngestionResult> IngestWebhookAsync(
        string? deliveryId,
        string? eventType,
        string? signatureHeader,
        byte[] rawBody,
        CancellationToken cancellationToken = default);
}
