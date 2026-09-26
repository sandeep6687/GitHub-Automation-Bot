using GitHubBot.Application.DTOs.Repository;

namespace GitHubBot.Application.Interfaces;

public interface IGitHubApiClient
{
    Task<IReadOnlyList<AvailableRepoDto>> GetUserRepositoriesAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<AvailableRepoDto?> GetRepositoryByIdAsync(string accessToken, long repositoryId, CancellationToken cancellationToken = default);
    Task<long> CreateWebhookAsync(string accessToken, string owner, string repo, string webhookUrl, string webhookSecret, CancellationToken cancellationToken = default);
    Task DeleteWebhookAsync(string accessToken, string owner, string repo, long webhookId, CancellationToken cancellationToken = default);
}
