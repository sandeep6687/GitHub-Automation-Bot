using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.RepositoryId)
            .HasColumnName("repository_id")
            .IsRequired();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.Priority)
            .HasColumnName("priority")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Index for rule lookup
        builder.HasIndex(r => new { r.RepositoryId, r.EventType })
            .HasDatabaseName("idx_rules_repo_event")
            .HasFilter("is_enabled = TRUE");

        builder.HasMany(r => r.Conditions)
            .WithOne(c => c.Rule)
            .HasForeignKey(c => c.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Actions)
            .WithOne(a => a.Rule)
            .HasForeignKey(a => a.RuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
