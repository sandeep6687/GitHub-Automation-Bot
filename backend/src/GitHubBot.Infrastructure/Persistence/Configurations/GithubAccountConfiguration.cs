using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class GithubAccountConfiguration : IEntityTypeConfiguration<GithubAccount>
{
    public void Configure(EntityTypeBuilder<GithubAccount> builder)
    {
        builder.ToTable("github_accounts");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");

        builder.Property(g => g.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(g => g.GithubUserId)
            .HasColumnName("github_user_id")
            .IsRequired();

        builder.Property(g => g.Login)
            .HasColumnName("login")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(g => g.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(g => g.EncryptedAccessToken)
            .HasColumnName("encrypted_access_token")
            .IsRequired();

        builder.Property(g => g.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(g => g.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Constraints
        builder.HasIndex(g => g.UserId)
            .IsUnique()
            .HasDatabaseName("uq_github_accounts_user_id");

        builder.HasIndex(g => g.GithubUserId)
            .IsUnique()
            .HasDatabaseName("uq_github_accounts_github_user_id");
    }
}
