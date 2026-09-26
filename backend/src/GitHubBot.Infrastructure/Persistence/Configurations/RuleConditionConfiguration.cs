using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class RuleConditionConfiguration : IEntityTypeConfiguration<RuleCondition>
{
    public void Configure(EntityTypeBuilder<RuleCondition> builder)
    {
        builder.ToTable("rule_conditions");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.RuleId)
            .HasColumnName("rule_id")
            .IsRequired();

        builder.Property(c => c.ConditionType)
            .HasColumnName("condition_type")
            .HasMaxLength(50)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(c => c.Field)
            .HasColumnName("field")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Value)
            .HasColumnName("value")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(c => c.CaseSensitive)
            .HasColumnName("case_sensitive")
            .HasDefaultValue(false)
            .IsRequired();
    }
}
