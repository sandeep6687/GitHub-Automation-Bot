using GitHubBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.IntegrationTests;

public static class PostgresTestHelper
{
    public const string ConnectionString = "Host=localhost;Port=5432;Database=github_bot_test;Username=postgres;Password=postgres";

    public static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    public static async Task ResetDatabaseAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM action_executions;
            DELETE FROM webhook_events;
            DELETE FROM rule_actions;
            DELETE FROM rule_conditions;
            DELETE FROM rules;
            DELETE FROM connected_repositories;
            DELETE FROM github_accounts;
            DELETE FROM users;
        ");
    }

    public static async Task<(Guid UserId, Guid RepositoryId)> SeedUserAndRepositoryAsync(long githubRepoId = 12345, string fullName = "test-owner/test-repo")
    {
        await using var context = CreateDbContext();
        var user = new GitHubBot.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            Email = $"testuser_{Guid.NewGuid().ToString("N")[..8]}@example.com"
        };
        context.Users.Add(user);

        var repo = new GitHubBot.Domain.Entities.ConnectedRepository
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            GithubRepositoryId = githubRepoId,
            FullName = fullName,
            Owner = fullName.Contains('/') ? fullName.Split('/')[0] : "owner",
            Name = fullName.Contains('/') ? fullName.Split('/')[1] : fullName,
            EncryptedWebhookSecret = "test_encrypted_secret"
        };
        context.ConnectedRepositories.Add(repo);

        await context.SaveChangesAsync();
        return (user.Id, repo.Id);
    }
}
