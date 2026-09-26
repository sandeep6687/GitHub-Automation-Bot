using System.Diagnostics;
using System.Text.Json;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Enums;

namespace GitHubBot.Infrastructure.ActionHandlers;

public class GitHubLabelActionHandler : IActionHandler
{
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly IGitHubTokenProvider _tokenProvider;

    public ActionType ActionType => ActionType.GithubAddLabel;

    public GitHubLabelActionHandler(
        IGitHubApiClient gitHubApiClient,
        IGitHubTokenProvider tokenProvider)
    {
        _gitHubApiClient = gitHubApiClient ?? throw new ArgumentNullException(nameof(gitHubApiClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        // 1. Validate configuration
        string? label = null;
        try
        {
            using var doc = JsonDocument.Parse(context.RuleAction.Configuration);
            if (doc.RootElement.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
            {
                label = labelProp.GetString();
            }
            else if (doc.RootElement.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
            {
                var first = labelsProp.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String)
                {
                    label = first.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return ActionResult.Failed("Invalid JSON in AddLabel configuration.", null, null, 0, isTransient: false);
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return ActionResult.Failed("AddLabel configuration must specify a non-empty 'label'.", null, null, 0, isTransient: false);
        }

        var normalizedLabel = label.Trim();

        // 2. Validate authoritative repository & event issue/PR context
        if (string.IsNullOrWhiteSpace(context.Repository.Owner) || string.IsNullOrWhiteSpace(context.Repository.Name))
        {
            return ActionResult.Failed("Authoritative ConnectedRepository has invalid owner or name.", null, null, 0, isTransient: false);
        }

        if (!context.IssueOrPrNumber.HasValue || context.IssueOrPrNumber.Value <= 0)
        {
            return ActionResult.Failed(
                $"Cannot execute AddLabel: WebhookEvent '{context.WebhookEvent.EventType}' does not have a valid issue or PR number.",
                null,
                null,
                0,
                isTransient: false);
        }

        // 3. Retrieve token via existing authenticated GitHub account service
        string token;
        try
        {
            token = await _tokenProvider.GetTokenForRepositoryAsync(context.Repository.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            return ActionResult.Failed($"Failed to retrieve GitHub access token: {ex.Message}", null, null, 0, isTransient: false);
        }

        var sanitizedRequest = JsonSerializer.Serialize(new { label = normalizedLabel });
        var sw = Stopwatch.StartNew();

        try
        {
            // 4. Call GitHub REST API (no DB lock held)
            var addedLabels = await _gitHubApiClient.AddLabelsAsync(
                token,
                context.Repository.Owner,
                context.Repository.Name,
                context.IssueOrPrNumber.Value,
                new[] { normalizedLabel },
                cancellationToken);

            sw.Stop();
            var sanitizedResponse = JsonSerializer.Serialize(new { labels = addedLabels });
            return ActionResult.Succeeded(sanitizedRequest, sanitizedResponse, (int)sw.ElapsedMilliseconds);
        }
        catch (GitHubApiException ex)
        {
            sw.Stop();
            return ActionResult.Failed(ex.Message, sanitizedRequest, ex.ResponseBody, (int)sw.ElapsedMilliseconds, isTransient: ex.IsTransient);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return ActionResult.Failed(ex.Message, sanitizedRequest, null, (int)sw.ElapsedMilliseconds, isTransient: true);
        }
    }
}
