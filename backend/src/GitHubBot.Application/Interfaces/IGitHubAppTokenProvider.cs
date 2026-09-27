using System.Threading;
using System.Threading.Tasks;

namespace GitHubBot.Application.Interfaces;

public interface IGitHubAppTokenProvider
{
    Task<string> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default);
    Task<long?> TryGetGitHubAppInstallationIdAsync(string owner, string repository, CancellationToken cancellationToken = default);
}
