using System.Text;
using System.Text.Json;
using GitHubBot.Application.DTOs.Webhook;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Application.Services;

public class WebhookIngestionService : IWebhookIngestionService
{
    private readonly IConnectedRepositoryRepository _connectedRepoRepository;
    private readonly IWebhookEventRepository _webhookEventRepository;
    private readonly IWebhookSignatureValidator _signatureValidator;
    private readonly ITokenEncryptionService _tokenEncryptionService;

    public WebhookIngestionService(
        IConnectedRepositoryRepository connectedRepoRepository,
        IWebhookEventRepository webhookEventRepository,
        IWebhookSignatureValidator signatureValidator,
        ITokenEncryptionService tokenEncryptionService)
    {
        _connectedRepoRepository = connectedRepoRepository;
        _webhookEventRepository = webhookEventRepository;
        _signatureValidator = signatureValidator;
        _tokenEncryptionService = tokenEncryptionService;
    }

    public async Task<WebhookIngestionResult> IngestWebhookAsync(
        string? deliveryId,
        string? eventType,
        string? signatureHeader,
        byte[] rawBody,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate required GitHub headers
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            return WebhookIngestionResult.MissingHeader("X-GitHub-Delivery");
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            return WebhookIngestionResult.MissingHeader("X-GitHub-Event");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return WebhookIngestionResult.InvalidSignature();
        }

        if (rawBody == null || rawBody.Length == 0)
        {
            return WebhookIngestionResult.MalformedPayload("Empty request body.");
        }

        // 2. Parse raw payload to identify the target repository
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            return WebhookIngestionResult.MalformedPayload(ex.Message);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("repository", out var repoElement))
            {
                return WebhookIngestionResult.RepositoryNotFound("Payload does not contain repository details");
            }

            long? githubRepoId = repoElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var id)
                ? id
                : null;

            string? fullName = repoElement.TryGetProperty("full_name", out var fnProp)
                ? fnProp.GetString()
                : null;

            if (!githubRepoId.HasValue && string.IsNullOrWhiteSpace(fullName))
            {
                return WebhookIngestionResult.RepositoryNotFound("Unable to determine repository identifier");
            }

            // 3. Authoritative repository lookup in database
            ConnectedRepository? repo = null;
            if (githubRepoId.HasValue)
            {
                repo = await _connectedRepoRepository.FindByGithubRepositoryIdAsync(githubRepoId.Value, cancellationToken);
            }

            if (repo == null && !string.IsNullOrWhiteSpace(fullName))
            {
                repo = await _connectedRepoRepository.FindByFullNameAsync(fullName, cancellationToken);
            }

            if (repo == null)
            {
                return WebhookIngestionResult.RepositoryNotFound(fullName ?? githubRepoId?.ToString() ?? "unknown");
            }

            if (!repo.IsActive)
            {
                return WebhookIngestionResult.RepositoryInactive(repo.FullName);
            }

            // 4. Decrypt webhook secret and verify HMAC signature
            var webhookSecret = _tokenEncryptionService.Decrypt(repo.EncryptedWebhookSecret);
            var isSignatureValid = _signatureValidator.Validate(rawBody, signatureHeader, webhookSecret);

            if (!isSignatureValid)
            {
                return WebhookIngestionResult.InvalidSignature();
            }

            // 5. Extract action if present (e.g., opened, closed, synchronize)
            string? action = doc.RootElement.TryGetProperty("action", out var actionProp)
                ? actionProp.GetString()
                : null;

            // 6. Build WebhookEvent entity
            var rawPayloadString = Encoding.UTF8.GetString(rawBody);
            var webhookEvent = new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                DeliveryId = deliveryId,
                EventType = eventType,
                Action = action,
                Status = EventStatus.Pending,
                RawPayload = rawPayloadString,
                ParsedData = null,
                AttemptCount = 0,
                MaxAttempts = 6,
                NextRetryAt = null,
                ClaimedAt = null,
                LastError = null,
                ProcessedAt = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // 7. Persist with idempotency check (database UNIQUE constraint on delivery_id is source of truth)
            var inserted = await _webhookEventRepository.TryInsertAsync(webhookEvent, cancellationToken);

            if (!inserted)
            {
                // Duplicate delivery already stored in database
                return WebhookIngestionResult.Duplicate(deliveryId);
            }

            return WebhookIngestionResult.Accepted(webhookEvent.Id, deliveryId);
        }
    }
}
