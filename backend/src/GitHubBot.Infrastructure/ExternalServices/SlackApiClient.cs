using System.Net;
using System.Net.Http.Json;
using GitHubBot.Application.Configuration;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace GitHubBot.Infrastructure.ExternalServices;

public class SlackApiClient : ISlackApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SlackOptions _options;

    public SlackApiClient(HttpClient httpClient, IOptions<SlackOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            throw new SlackApiException(
                HttpStatusCode.BadRequest,
                "Slack Webhook URL is not configured on the server. Please set Slack__WebhookUrl in configuration.",
                isTransient: false);
        }

        if (!Uri.TryCreate(_options.WebhookUrl, UriKind.Absolute, out var webhookUri) ||
            (webhookUri.Scheme != Uri.UriSchemeHttp && webhookUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new SlackApiException(
                HttpStatusCode.BadRequest,
                "Configured Slack Webhook URL is invalid.",
                isTransient: false);
        }

        var payload = new { text = message };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SlackApiException(
                HttpStatusCode.RequestTimeout,
                "Slack HTTP request timed out.",
                isTransient: true,
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlackApiException(
                ex.StatusCode ?? HttpStatusCode.ServiceUnavailable,
                "Failed to connect to Slack webhook endpoint.",
                isTransient: true,
                ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var statusCode = response.StatusCode;
            var isTransient = (int)statusCode == 429 || (int)statusCode >= 500;

            var errorMessage = statusCode switch
            {
                HttpStatusCode.BadRequest => "Slack rejected request (400 Bad Request). Invalid payload format or channel.",
                HttpStatusCode.Unauthorized => "Slack authentication failed (401 Unauthorized). Webhook token is invalid.",
                HttpStatusCode.Forbidden => "Slack webhook access forbidden (403 Forbidden).",
                HttpStatusCode.NotFound => "Slack Incoming Webhook URL not found (404 Not Found). Webhook may have been revoked.",
                (HttpStatusCode)429 => "Slack rate limit exceeded (429 Too Many Requests).",
                HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout => $"Slack server error ({(int)statusCode}).",
                _ => $"Slack API request failed with status code {(int)statusCode}."
            };

            throw new SlackApiException(statusCode, errorMessage, isTransient);
        }
    }
}
