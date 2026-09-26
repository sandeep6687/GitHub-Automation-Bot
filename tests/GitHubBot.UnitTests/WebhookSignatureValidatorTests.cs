using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GitHubBot.Application.Services;

namespace GitHubBot.UnitTests;

public class WebhookSignatureValidatorTests
{
    private readonly WebhookSignatureValidator _validator = new();
    private const string Secret = "super_secure_webhook_secret_12345";

    public static string ComputeGitHubSignature(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(body);
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [Fact]
    public void Validate_ValidSignature_ShouldReturnTrue()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\",\"repository\":{\"id\":100}}");
        var signature = ComputeGitHubSignature(Secret, body);

        var result = _validator.Validate(body, signature, Secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidSignature_CaseInsensitivePrefixAndHex_ShouldReturnTrue()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(body);
        var signature = $"SHA256={Convert.ToHexString(hash).ToUpperInvariant()}";

        var result = _validator.Validate(body, signature, Secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_TamperedBody_ShouldReturnFalse()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        var tamperedBody = Encoding.UTF8.GetBytes("{\"action\":\"closed\"}");
        var signature = ComputeGitHubSignature(Secret, originalBody);

        var result = _validator.Validate(tamperedBody, signature, Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WrongSecret_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        var signature = ComputeGitHubSignature(Secret, body);

        var result = _validator.Validate(body, signature, "different_secret_key");

        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingSignature_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");

        _validator.Validate(body, null, Secret).Should().BeFalse();
        _validator.Validate(body, "", Secret).Should().BeFalse();
        _validator.Validate(body, "   ", Secret).Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingPrefix_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hexOnly = Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();

        var result = _validator.Validate(body, hexOnly, Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_MalformedHex_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        var malformedSignature = "sha256=not_valid_hex_characters_zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz";

        var result = _validator.Validate(body, malformedSignature, Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShortOrTruncatedSignature_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        var shortSignature = "sha256=1234abcd";

        var result = _validator.Validate(body, shortSignature, Secret);

        result.Should().BeFalse();
    }
}
