using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using GitHubBot.Application.Configuration;
using GitHubBot.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GitHubBot.Infrastructure.ExternalServices;

public class GitHubAppTokenProvider : IGitHubAppTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GitHubAppOptions _options;
    private readonly ILogger<GitHubAppTokenProvider> _logger;

    public GitHubAppTokenProvider(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<GitHubAppOptions> options,
        ILogger<GitHubAppTokenProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubAutomationBot/1.0");
        }
    }

    public async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default)
    {
        if (!_options.AppId.HasValue || string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            throw new InvalidOperationException("GitHub App is not configured properly.");
        }

        string cacheKey = $"GitHubAppInstallationToken_{installationId}";
        if (_cache.TryGetValue(cacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
        {
            return cachedToken;
        }

        string jwt = GenerateGitHubAppJwt(_options.AppId.Value, _options.PrivateKey);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/app/installations/{installationId}/access_tokens");
        
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to obtain installation token for installation {installationId}. Status code: {response.StatusCode}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<GitHubInstallationTokenResponse>(cancellationToken: cancellationToken);
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.Token))
        {
            throw new InvalidOperationException("GitHub API returned invalid installation token response.");
        }

        var expireAt = tokenResponse.ExpiresAt.ToUniversalTime().AddMinutes(-5);
        if (expireAt <= DateTime.UtcNow)
        {
            expireAt = DateTime.UtcNow.AddMinutes(1);
        }

        _cache.Set(cacheKey, tokenResponse.Token, new DateTimeOffset(expireAt));

        return tokenResponse.Token;
    }

    public async Task<long?> TryGetGitHubAppInstallationIdAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        if (!_options.AppId.HasValue || string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            return null;
        }

        try
        {
            string jwt = GenerateGitHubAppJwt(_options.AppId.Value, _options.PrivateKey);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{repository}/installation");
            
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API failed to get installation for {Owner}/{Repository}: {StatusCode}", owner, repository, response.StatusCode);
                return null;
            }

            var installation = await response.Content.ReadFromJsonAsync<GitHubInstallationModel>(cancellationToken: cancellationToken);
            return installation?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synchronize GitHub App installation for repository {Owner}/{Repository}", owner, repository);
            return null;
        }
    }

    private string GenerateGitHubAppJwt(long appId, string privateKey)
    {
        using var rsa = RSA.Create();
        
        var pk = privateKey.Replace("\\n", "\n").Trim();
        if (pk.Contains("BEGIN RSA PRIVATE KEY") || pk.Contains("BEGIN PRIVATE KEY"))
        {
            rsa.ImportFromPem(pk);
        }
        else
        {
            throw new InvalidOperationException("GitHub App Private Key must be in PEM format.");
        }

        var securityKey = new RsaSecurityKey(rsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        
        var header = new JwtHeader(credentials);
        var payload = new JwtPayload
        {
            { "iat", new DateTimeOffset(now.AddSeconds(-60)).ToUnixTimeSeconds() },
            { "exp", new DateTimeOffset(now.AddMinutes(10)).ToUnixTimeSeconds() },
            { "iss", appId.ToString() }
        };

        var jwtToken = new JwtSecurityToken(header, payload);
        var handler = new JwtSecurityTokenHandler();
        
        return handler.WriteToken(jwtToken);
    }
    
    private class GitHubInstallationTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; set; }
    }

    private class GitHubInstallationModel
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }
}
