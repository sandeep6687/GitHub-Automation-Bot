namespace GitHubBot.Application.Interfaces;

public interface IGitHubTokenProvider
{
    Task<string> GetTokenForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default);
}
