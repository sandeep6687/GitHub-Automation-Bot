using System.Diagnostics;
using System.Text.Json;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Helpers;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Enums;

namespace GitHubBot.Infrastructure.ActionHandlers;

public class GitHubCommentActionHandler : IActionHandler
{
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly IGitHubTokenProvider _tokenProvider;

    public ActionType ActionType => ActionType.GithubAddComment;

    public GitHubCommentActionHandler(
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
        string? body = null;
        try
        {
            using var doc = JsonDocument.Parse(context.RuleAction.Configuration);
            if (doc.RootElement.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String)
            {
                body = bodyProp.GetString();
            }
            else if (doc.RootElement.TryGetProperty("comment", out var commentProp) && commentProp.ValueKind == JsonValueKind.String)
            {
                body = commentProp.GetString();
            }
        }
        catch (JsonException)
        {
            return ActionResult.Failed("Invalid JSON in AddComment configuration.", null, null, 0, isTransient: false);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return ActionResult.Failed("AddComment configuration must specify a non-empty 'body'.", null, null, 0, isTransient: false);
        }

        var trimmedBody = body.Trim();

        // 2. Validate authoritative repository & event issue/PR context
        if (string.IsNullOrWhiteSpace(context.Repository.Owner) || string.IsNullOrWhiteSpace(context.Repository.Name))
        {
            return ActionResult.Failed("Authoritative ConnectedRepository has invalid owner or name.", null, null, 0, isTransient: false);
        }

        if (!context.IssueOrPrNumber.HasValue || context.IssueOrPrNumber.Value <= 0)
        {
            return ActionResult.Failed(
                $"Cannot execute AddComment: WebhookEvent '{context.WebhookEvent.EventType}' does not have a valid issue or PR number.",
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

        var sanitizedRequest = JsonSerializer.Serialize(new { body = trimmedBody });
        var sw = Stopwatch.StartNew();

        try
        {
            // 4. Idempotency Check: Fetch existing comments to check for marker before creating a duplicate
            var existingComments = await _gitHubApiClient.GetIssueCommentsAsync(
                token,
                context.Repository.Owner,
                context.Repository.Name,
                context.IssueOrPrNumber.Value,
                cancellationToken);

            var existingCommentWithMarker = existingComments.FirstOrDefault(c =>
                CommentIdempotencyHelper.ContainsMarker(c.Body, context.WebhookEvent.Id, context.RuleAction.Id));

            if (existingCommentWithMarker != null)
            {
                sw.Stop();
                var idempotentResponse = JsonSerializer.Serialize(new
                {
                    commentId = existingCommentWithMarker.Id,
                    note = "Comment already created in previous attempt (idempotency marker detected)"
                });

                return ActionResult.Succeeded(sanitizedRequest, idempotentResponse, (int)sw.ElapsedMilliseconds);
            }

            // 5. Attach deterministic idempotency marker to comment body
            var bodyWithMarker = CommentIdempotencyHelper.AttachMarker(
                trimmedBody,
                context.WebhookEvent.Id,
                context.RuleAction.Id);

            // 6. Create comment via GitHub REST API (no DB lock held)
            var createdComment = await _gitHubApiClient.AddCommentAsync(
                token,
                context.Repository.Owner,
                context.Repository.Name,
                context.IssueOrPrNumber.Value,
                bodyWithMarker,
                cancellationToken);

            sw.Stop();
            var sanitizedResponse = JsonSerializer.Serialize(new
            {
                commentId = createdComment.Id,
                url = createdComment.HtmlUrl
            });

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
