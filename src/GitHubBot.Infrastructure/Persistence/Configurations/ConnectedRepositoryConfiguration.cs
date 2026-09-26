using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class ConnectedRepositoryConfiguration : IEntityTypeConfiguration<ConnectedRepository>
{
    public void Configure(EntityTypeBuilder<ConnectedRepository> builder)
    {
        builder.ToTable("connected_repositories");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(r => r.GithubRepositoryId)
            .HasColumnName("github_repository_id")
            .IsRequired();

        builder.Property(r => r.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.Owner)
            .HasColumnName("owner")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.DefaultBranch)
            .HasColumnName("default_branch")
            .HasMaxLength(100)
            .HasDefaultValue("main")
            .IsRequired();

        builder.Property(r => r.WebhookId)
            .HasColumnName("webhook_id");

        builder.Property(r => r.EncryptedWebhookSecret)
            .HasColumnName("encrypted_webhook_secret")
            .IsRequired();

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Constraints & Indexes
        builder.HasIndex(r => r.GithubRepositoryId)
            .IsUnique()
            .HasDatabaseName("uq_connected_repositories_github_repo_id");

        builder.HasIndex(r => r.FullName)
            .HasDatabaseName("idx_connected_repositories_full_name");

        builder.HasMany(r => r.Rules)
            .WithOne(rule => rule.Repository)
            .HasForeignKey(rule => rule.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.WebhookEvents)
            .WithOne(w => w.Repository)
            .HasForeignKey(w => w.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
