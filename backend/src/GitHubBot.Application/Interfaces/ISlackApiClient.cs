namespace GitHubBot.Application.Interfaces;

public interface ISlackApiClient
{
    Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
}
