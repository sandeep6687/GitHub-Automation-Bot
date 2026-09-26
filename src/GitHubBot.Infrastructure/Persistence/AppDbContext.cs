using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<GithubAccount> GithubAccounts => Set<GithubAccount>();
    public DbSet<ConnectedRepository> ConnectedRepositories => Set<ConnectedRepository>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<RuleCondition> RuleConditions => Set<RuleCondition>();
    public DbSet<RuleAction> RuleActions => Set<RuleAction>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<ActionExecution> ActionExecutions => Set<ActionExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
