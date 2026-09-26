namespace GitHubBot.Application.DTOs.Webhook;

public enum WebhookIngestionStatus
{
    Accepted,
    Duplicate,
    InvalidSignature,
    MissingHeader,
    RepositoryNotFound,
    RepositoryInactive,
    MalformedPayload
}

public class WebhookIngestionResult
{
    public WebhookIngestionStatus Status { get; init; }
    public Guid? EventId { get; init; }
    public string? DeliveryId { get; init; }
    public string? ErrorMessage { get; init; }

    public static WebhookIngestionResult Accepted(Guid eventId, string deliveryId) => new()
    {
        Status = WebhookIngestionStatus.Accepted,
        EventId = eventId,
        DeliveryId = deliveryId
    };

    public static WebhookIngestionResult Duplicate(string deliveryId) => new()
    {
        Status = WebhookIngestionStatus.Duplicate,
        DeliveryId = deliveryId
    };

    public static WebhookIngestionResult InvalidSignature() => new()
    {
        Status = WebhookIngestionStatus.InvalidSignature,
        ErrorMessage = "Invalid or missing HMAC-SHA256 signature."
    };

    public static WebhookIngestionResult MissingHeader(string headerName) => new()
    {
        Status = WebhookIngestionStatus.MissingHeader,
        ErrorMessage = $"Missing required header: {headerName}"
    };

    public static WebhookIngestionResult RepositoryNotFound(string repoIdentifier) => new()
    {
        Status = WebhookIngestionStatus.RepositoryNotFound,
        ErrorMessage = $"Connected repository not found: {repoIdentifier}"
    };

    public static WebhookIngestionResult RepositoryInactive(string repoIdentifier) => new()
    {
        Status = WebhookIngestionStatus.RepositoryInactive,
        ErrorMessage = $"Connected repository is inactive: {repoIdentifier}"
    };

    public static WebhookIngestionResult MalformedPayload(string details) => new()
    {
        Status = WebhookIngestionStatus.MalformedPayload,
        ErrorMessage = $"Malformed webhook JSON payload: {details}"
    };
}
