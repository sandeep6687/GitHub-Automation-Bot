// Infrastructure layer — depends on Domain, Application.
// Contains: EF Core DbContext, repositories, action handlers, background worker, external clients.
namespace GitHubBot.Infrastructure;

public static class InfrastructureAssemblyMarker
{
    // Explicit references ensure compiler does not trim unused assembly references in Phase 0
    public static readonly Type DomainReference = typeof(GitHubBot.Domain.DomainAssemblyMarker);
    public static readonly Type ApplicationReference = typeof(GitHubBot.Application.ApplicationAssemblyMarker);
}
