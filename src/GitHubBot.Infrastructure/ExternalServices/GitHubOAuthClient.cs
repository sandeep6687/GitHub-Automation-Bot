using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GitHubBot.Application.DTOs.Auth;
using GitHubBot.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GitHubBot.Infrastructure.ExternalServices;

public class GitHubOAuthClient : IGitHubOAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;

    public GitHubOAuthClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _clientId = configuration["GitHub:ClientId"] ?? string.Empty;
        _clientSecret = configuration["GitHub:ClientSecret"] ?? string.Empty;
        _redirectUri = configuration["GitHub:RedirectUri"] ?? string.Empty;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubAutomationBot/1.0");
        }
    }

    public string GetAuthorizationUrl(string state)
    {
        var encodedRedirect = Uri.EscapeDataString(_redirectUri);
        var encodedState = Uri.EscapeDataString(state);
        return $"https://github.com/login/oauth/authorize?client_id={_clientId}&redirect_uri={encodedRedirect}&scope=read:user,repo&state={encodedState}";
    }

    public async Task<string> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var content = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["redirect_uri"] = _redirectUri
        };

        request.Content = new FormUrlEncodedContent(content);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub OAuth token exchange failed with status code {response.StatusCode}.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(
            cancellationToken: cancellationToken);

        if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
        {
            var errorMsg = tokenResponse?.ErrorDescription ?? tokenResponse?.Error ?? "Unknown error";
            throw new InvalidOperationException($"GitHub OAuth token exchange rejected: {errorMsg}");
        }

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("GitHub OAuth response did not contain an access token.");
        }

        return tokenResponse.AccessToken;
    }

    public async Task<GitHubUserResponse> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub API user retrieval failed with status code {response.StatusCode}.");
        }

        var user = await response.Content.ReadFromJsonAsync<GitHubUserResponse>(
            cancellationToken: cancellationToken);

        if (user == null || user.Id <= 0)
        {
            throw new InvalidOperationException("Failed to deserialize GitHub user profile response.");
        }

        return user;
    }
}
