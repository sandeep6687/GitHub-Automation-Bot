using GitHubBot.Application.DTOs.GitHub;
using GitHubBot.Application.DTOs.Repository;

namespace GitHubBot.Application.Interfaces;

public interface IGitHubApiClient
{
    Task<IReadOnlyList<AvailableRepoDto>> GetUserRepositoriesAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<AvailableRepoDto?> GetRepositoryByIdAsync(string accessToken, long repositoryId, CancellationToken cancellationToken = default);
    Task<long?> GetAppInstallationIdForRepositoryAsync(string accessToken, string owner, string repo, CancellationToken cancellationToken = default);
    Task<long> CreateWebhookAsync(string accessToken, string owner, string repo, string webhookUrl, string webhookSecret, CancellationToken cancellationToken = default);
    Task DeleteWebhookAsync(string accessToken, string owner, string repo, long webhookId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> AddLabelsAsync(string accessToken, string owner, string repo, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken = default);
    Task<GitHubCommentDto> AddCommentAsync(string accessToken, string owner, string repo, int issueOrPrNumber, string commentBody, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitHubCommentDto>> GetIssueCommentsAsync(string accessToken, string owner, string repo, int issueOrPrNumber, CancellationToken cancellationToken = default);
}
