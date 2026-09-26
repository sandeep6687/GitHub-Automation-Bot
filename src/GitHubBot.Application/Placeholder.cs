// Application layer — depends on Domain only.
// Contains: services, DTOs, IActionHandler, ActionDispatcher.
namespace GitHubBot.Application;

public static class ApplicationAssemblyMarker
{
    // Explicit reference ensures compiler does not trim unused assembly reference in Phase 0
    public static readonly Type DomainReference = typeof(GitHubBot.Domain.DomainAssemblyMarker);
}
