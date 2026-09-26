namespace GitHubBot.Application.Interfaces;

public interface IWebhookSignatureValidator
{
    bool Validate(byte[] rawBody, string? signatureHeader, string secret);
}
