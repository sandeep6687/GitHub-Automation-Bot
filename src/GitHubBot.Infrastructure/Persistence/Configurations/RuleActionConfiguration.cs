using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class RuleActionConfiguration : IEntityTypeConfiguration<RuleAction>
{
    public void Configure(EntityTypeBuilder<RuleAction> builder)
    {
        builder.ToTable("rule_actions");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.RuleId)
            .HasColumnName("rule_id")
            .IsRequired();

        builder.Property(a => a.ActionType)
            .HasColumnName("action_type")
            .HasMaxLength(50)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.Configuration)
            .HasColumnName("configuration")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.ExecutionOrder)
            .HasColumnName("execution_order")
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasMany(a => a.ActionExecutions)
            .WithOne(e => e.RuleAction)
            .HasForeignKey(e => e.RuleActionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
