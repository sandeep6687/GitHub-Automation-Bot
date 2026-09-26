using System.Security.Cryptography;
using System.Text;
using GitHubBot.Application.Interfaces;

namespace GitHubBot.Application.Services;

public class WebhookSignatureValidator : IWebhookSignatureValidator
{
    private const string Sha256Prefix = "sha256=";

    public bool Validate(byte[] rawBody, string? signatureHeader, string secret)
    {
        if (rawBody == null || string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        if (!signatureHeader.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hexSignature = signatureHeader[Sha256Prefix.Length..].Trim();
        if (hexSignature.Length != 64)
        {
            return false;
        }

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(hexSignature);
        }
        catch (FormatException)
        {
            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(secretBytes);
        var computedHash = hmac.ComputeHash(rawBody);

        // Constant-time comparison prevents timing attacks
        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }
}
