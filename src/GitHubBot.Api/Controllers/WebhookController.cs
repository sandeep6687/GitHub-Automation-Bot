using GitHubBot.Application.DTOs.Webhook;
using GitHubBot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitHubBot.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
[AllowAnonymous] // Public endpoint for incoming GitHub webhook deliveries
public class WebhookController : ControllerBase
{
    private readonly IWebhookIngestionService _ingestionService;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWebhookIngestionService ingestionService,
        ILogger<WebhookController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [HttpPost("github")]
    public async Task<IActionResult> HandleGitHubWebhook(CancellationToken cancellationToken)
    {
        // 1. Read required GitHub headers
        var deliveryId = Request.Headers["X-GitHub-Delivery"].FirstOrDefault();
        var eventType = Request.Headers["X-GitHub-Event"].FirstOrDefault();
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        var correlationId = HttpContext.TraceIdentifier;

        _logger.LogInformation(
            "Webhook delivery received. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}, EventType: {EventType}",
            correlationId, deliveryId, eventType);

        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            _logger.LogWarning("Webhook delivery missing X-GitHub-Delivery header. CorrelationId: {CorrelationId}", correlationId);
            return BadRequest(new { error = "Missing X-GitHub-Delivery header." });
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("Webhook delivery missing X-GitHub-Event header. CorrelationId: {CorrelationId}", correlationId);
            return BadRequest(new { error = "Missing X-GitHub-Event header." });
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("Webhook delivery missing X-Hub-Signature-256 header. CorrelationId: {CorrelationId}", correlationId);
            return Unauthorized(new { error = "Missing signature header." });
        }

        // 2. Read raw request body as bytes for HMAC verification
        byte[] rawBytes;
        using (var memoryStream = new MemoryStream())
        {
            await Request.Body.CopyToAsync(memoryStream, cancellationToken);
            rawBytes = memoryStream.ToArray();
        }

        if (rawBytes.Length == 0)
        {
            _logger.LogWarning("Webhook request body is empty. CorrelationId: {CorrelationId}", correlationId);
            return BadRequest(new { error = "Request body is empty." });
        }

        // 3. Process ingestion (HMAC validation, repo identification, deduplication, persistence)
        var result = await _ingestionService.IngestWebhookAsync(
            deliveryId,
            eventType,
            signature,
            rawBytes,
            cancellationToken);

        switch (result.Status)
        {
            case WebhookIngestionStatus.Accepted:
                _logger.LogInformation(
                    "Webhook accepted. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}, EventId: {EventId}",
                    correlationId, deliveryId, result.EventId);
                return StatusCode(StatusCodes.Status202Accepted, new
                {
                    status = "accepted",
                    eventId = result.EventId,
                    deliveryId = result.DeliveryId
                });

            case WebhookIngestionStatus.Duplicate:
                _logger.LogInformation(
                    "Duplicate webhook delivery acknowledged. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}",
                    correlationId, deliveryId);
                return Ok(new
                {
                    status = "duplicate",
                    deliveryId = result.DeliveryId
                });

            case WebhookIngestionStatus.InvalidSignature:
                _logger.LogWarning(
                    "Webhook signature verification failed. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}",
                    correlationId, deliveryId);
                return Unauthorized(new { error = "Invalid signature." });

            case WebhookIngestionStatus.RepositoryNotFound:
                _logger.LogWarning(
                    "Webhook target repository not found. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}",
                    correlationId, deliveryId);
                return NotFound(new { error = result.ErrorMessage });

            case WebhookIngestionStatus.RepositoryInactive:
                _logger.LogWarning(
                    "Webhook target repository is inactive. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}",
                    correlationId, deliveryId);
                return BadRequest(new { error = result.ErrorMessage });

            case WebhookIngestionStatus.MalformedPayload:
            case WebhookIngestionStatus.MissingHeader:
                _logger.LogWarning(
                    "Webhook rejected: {Error}. CorrelationId: {CorrelationId}, DeliveryId: {DeliveryId}",
                    result.ErrorMessage, correlationId, deliveryId);
                return BadRequest(new { error = result.ErrorMessage });

            default:
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected ingestion status." });
        }
    }
}
