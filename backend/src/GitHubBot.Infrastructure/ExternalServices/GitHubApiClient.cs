using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitHubBot.Application.DTOs.GitHub;
using GitHubBot.Application.DTOs.Repository;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Interfaces;

namespace GitHubBot.Infrastructure.ExternalServices;

public class GitHubApiClient : IGitHubApiClient
{
    private readonly HttpClient _httpClient;

    public GitHubApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubAutomationBot/1.0");
        }
    }

    private void SetAuthHeader(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<IReadOnlyList<AvailableRepoDto>> GetUserRepositoriesAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member");
        SetAuthHeader(request, accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub API failed to list repositories: {response.StatusCode}");
        }

        var githubRepos = await response.Content.ReadFromJsonAsync<List<GitHubRepoModel>>(
            cancellationToken: cancellationToken) ?? new List<GitHubRepoModel>();

        return githubRepos.Select(r => new AvailableRepoDto
        {
            Id = r.Id,
            FullName = r.FullName,
            Name = r.Name,
            Owner = r.Owner?.Login ?? string.Empty,
            DefaultBranch = r.DefaultBranch ?? "main",
            IsPrivate = r.Private,
            IsConnected = false
        }).ToList();
    }

    public async Task<AvailableRepoDto?> GetRepositoryByIdAsync(
        string accessToken,
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repositories/{repositoryId}");
        SetAuthHeader(request, accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub API failed to get repository {repositoryId}: {response.StatusCode}");
        }

        var r = await response.Content.ReadFromJsonAsync<GitHubRepoModel>(cancellationToken: cancellationToken);
        if (r == null)
            return null;

        return new AvailableRepoDto
        {
            Id = r.Id,
            FullName = r.FullName,
            Name = r.Name,
            Owner = r.Owner?.Login ?? string.Empty,
            DefaultBranch = r.DefaultBranch ?? "main",
            IsPrivate = r.Private,
            IsConnected = false
        };
    }

    public async Task<long?> GetAppInstallationIdForRepositoryAsync(
        string accessToken,
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repo}/installation");
        SetAuthHeader(request, accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // App is not installed on this repository
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub API failed to get installation for {owner}/{repo}: {response.StatusCode}");
        }

        var installation = await response.Content.ReadFromJsonAsync<GitHubInstallationModel>(cancellationToken: cancellationToken);
        return installation?.Id;
    }

    public async Task<long> CreateWebhookAsync(
        string accessToken,
        string owner,
        string repo,
        string webhookUrl,
        string webhookSecret,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{owner}/{repo}/hooks");
        SetAuthHeader(request, accessToken);

        var payload = new
        {
            name = "web",
            active = true,
            events = new[] { "issues", "pull_request", "push" },
            config = new
            {
                url = webhookUrl,
                content_type = "json",
                secret = webhookSecret,
                insecure_ssl = "0"
            }
        };

        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"GitHub API failed to create webhook: {response.StatusCode} - {errorBody}");
        }

        var hookResponse = await response.Content.ReadFromJsonAsync<GitHubWebhookResponse>(
            cancellationToken: cancellationToken);

        if (hookResponse == null || hookResponse.Id <= 0)
        {
            throw new InvalidOperationException("Failed to parse GitHub webhook creation response.");
        }

        return hookResponse.Id;
    }

    public async Task DeleteWebhookAsync(
        string accessToken,
        string owner,
        string repo,
        long webhookId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"https://api.github.com/repos/{owner}/{repo}/hooks/{webhookId}");
        SetAuthHeader(request, accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        // Ignore 404 if webhook was already removed on GitHub
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new HttpRequestException($"GitHub API failed to delete webhook {webhookId}: {response.StatusCode}");
        }
    }

    public async Task<IReadOnlyList<string>> AddLabelsAsync(
        string accessToken,
        string owner,
        string repo,
        int issueOrPrNumber,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{owner}/{repo}/issues/{issueOrPrNumber}/labels");
        SetAuthHeader(request, accessToken);
        request.Content = JsonContent.Create(new { labels });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, $"adding labels to {owner}/{repo}#{issueOrPrNumber}", cancellationToken);

        var labelModels = await response.Content.ReadFromJsonAsync<List<GitHubLabelResponseModel>>(
            cancellationToken: cancellationToken);

        return labelModels?.Select(l => l.Name).ToList() ?? labels.ToList();
    }

    public async Task<GitHubCommentDto> AddCommentAsync(
        string accessToken,
        string owner,
        string repo,
        int issueOrPrNumber,
        string commentBody,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{owner}/{repo}/issues/{issueOrPrNumber}/comments");
        SetAuthHeader(request, accessToken);
        request.Content = JsonContent.Create(new { body = commentBody });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, $"adding comment to {owner}/{repo}#{issueOrPrNumber}", cancellationToken);

        var comment = await response.Content.ReadFromJsonAsync<GitHubCommentDto>(
            cancellationToken: cancellationToken);

        return comment ?? new GitHubCommentDto { Body = commentBody };
    }

    public async Task<IReadOnlyList<GitHubCommentDto>> GetIssueCommentsAsync(
        string accessToken,
        string owner,
        string repo,
        int issueOrPrNumber,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repo}/issues/{issueOrPrNumber}/comments?per_page=100");
        SetAuthHeader(request, accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, $"getting comments for {owner}/{repo}#{issueOrPrNumber}", cancellationToken);

        var comments = await response.Content.ReadFromJsonAsync<List<GitHubCommentDto>>(
            cancellationToken: cancellationToken);

        return comments ?? new List<GitHubCommentDto>();
    }

    private static async Task EnsureSuccessStatusCodeAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = response.StatusCode;
        string? errorBody = null;
        try
        {
            errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // Ignore response read errors
        }

        var message = statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "GitHub API authentication failed (401). Access token is invalid or expired.",
            System.Net.HttpStatusCode.Forbidden =>
                "GitHub API access forbidden (403). Insufficient permissions or rate limit exceeded.",
            System.Net.HttpStatusCode.NotFound =>
                $"GitHub API resource not found (404) during {operation}.",
            System.Net.HttpStatusCode.Conflict =>
                $"GitHub API resource conflict (409) during {operation}.",
            (System.Net.HttpStatusCode)429 =>
                "GitHub API rate limit exceeded (429).",
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout =>
                $"GitHub API server error ({(int)statusCode}) during {operation}.",
            _ => $"GitHub API request failed with status code {statusCode} during {operation}."
        };

        throw new GitHubApiException(statusCode, message, errorBody);
    }

    private class GitHubRepoModel
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("private")]
        public bool Private { get; set; }

        [JsonPropertyName("default_branch")]
        public string? DefaultBranch { get; set; }

        [JsonPropertyName("owner")]
        public GitHubOwnerModel? Owner { get; set; }
    }

    private class GitHubOwnerModel
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;
    }

    private class GitHubWebhookResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    private class GitHubLabelResponseModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private class GitHubInstallationModel
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }
}
