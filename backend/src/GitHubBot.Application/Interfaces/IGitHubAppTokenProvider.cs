namespace GitHubBot.Application.Interfaces;

public interface IGitHubAppTokenProvider
{
    Task<string> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default);
}
