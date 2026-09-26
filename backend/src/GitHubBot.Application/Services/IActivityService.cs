using GitHubBot.Application.DTOs.Activity;

namespace GitHubBot.Application.Services;

public interface IActivityService
{
    Task<ActivityResponseDto> GetActivityAsync(
        Guid userId,
        Guid repositoryId,
        int limit = 50,
        DateTime? before = null,
        string? status = null,
        string? eventType = null,
        CancellationToken cancellationToken = default);
}
