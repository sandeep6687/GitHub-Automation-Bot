using System.Security.Cryptography;
using System.Text;
using GitHubBot.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GitHubBot.Infrastructure.Security;

public class TokenEncryptionService : ITokenEncryptionService
{
    private readonly byte[] _key;

    public TokenEncryptionService(IConfiguration configuration)
    {
        var base64Key = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            // For development fallback: derive 32-byte key
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("DevFallbackKeyForEncryptionMustBe32BytesLong"));
        }
        else
        {
            try
            {
                var decoded = Convert.FromBase64String(base64Key);
                if (decoded.Length != 32)
                {
                    _key = SHA256.HashData(decoded);
                }
                else
                {
                    _key = decoded;
                }
            }
            catch (FormatException)
            {
                _key = SHA256.HashData(Encoding.UTF8.GetBytes(base64Key));
            }
        }
    }

    public TokenEncryptionService(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Encryption key must be exactly 32 bytes (256 bits).", nameof(key));

        _key = key;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var tag = new byte[16];
        var cipherBytes = new byte[plainBytes.Length];

        using var aesGcm = new AesGcm(_key, 16);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Payload format: nonce (12) + tag (16) + ciphertext (N)
        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        var combined = Convert.FromBase64String(cipherText);
        if (combined.Length < 28)
            throw new CryptographicException("Ciphertext payload is truncated or invalid.");

        var nonce = new byte[12];
        var tag = new byte[16];
        var cipherLength = combined.Length - 28;
        var cipherBytes = new byte[cipherLength];
        var plainBytes = new byte[cipherLength];

        Buffer.BlockCopy(combined, 0, nonce, 0, 12);
        Buffer.BlockCopy(combined, 12, tag, 0, 16);
        Buffer.BlockCopy(combined, 28, cipherBytes, 0, cipherLength);

        using var aesGcm = new AesGcm(_key, 16);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
