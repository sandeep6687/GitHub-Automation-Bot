using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GitHubBot.Infrastructure.Security;

namespace GitHubBot.UnitTests;

public class TokenEncryptionServiceTests
{
    [Fact]
    public void EncryptAndDecrypt_ShouldRestoreOriginalPlainText()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var service = new TokenEncryptionService(key);

        var secretToken = "ghp_1234567890abcdefghijklmnopqrstuvwxyz";

        var encrypted = service.Encrypt(secretToken);
        encrypted.Should().NotBeNullOrWhiteSpace();
        encrypted.Should().NotBe(secretToken);

        var decrypted = service.Decrypt(encrypted);
        decrypted.Should().Be(secretToken);
    }

    [Fact]
    public void Encrypt_ShouldProduceDifferentCiphertexts_ForSamePlainTextDueToRandomNonce()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var service = new TokenEncryptionService(key);

        var plainText = "gho_test_token_value";

        var cipher1 = service.Encrypt(plainText);
        var cipher2 = service.Encrypt(plainText);

        cipher1.Should().NotBe(cipher2);
        service.Decrypt(cipher1).Should().Be(plainText);
        service.Decrypt(cipher2).Should().Be(plainText);
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenCiphertextIsTamperedWith()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var service = new TokenEncryptionService(key);

        var encrypted = service.Encrypt("important_token");
        var rawBytes = Convert.FromBase64String(encrypted);

        // Tamper with last byte of ciphertext
        rawBytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(rawBytes);

        var act = () => service.Decrypt(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenCiphertextIsTruncated()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var service = new TokenEncryptionService(key);

        var shortPayload = Convert.ToBase64String(new byte[10]);

        var act = () => service.Decrypt(shortPayload);
        act.Should().Throw<CryptographicException>();
    }
}
