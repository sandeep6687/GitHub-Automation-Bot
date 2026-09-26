using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class ActionExecutionConfiguration : IEntityTypeConfiguration<ActionExecution>
{
    public void Configure(EntityTypeBuilder<ActionExecution> builder)
    {
        builder.ToTable("action_executions");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.WebhookEventId)
            .HasColumnName("webhook_event_id")
            .IsRequired();

        builder.Property(a => a.RuleActionId)
            .HasColumnName("rule_action_id");

        builder.Property(a => a.ActionType)
            .HasColumnName("action_type")
            .HasMaxLength(50)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.RequestPayload)
            .HasColumnName("request_payload")
            .HasColumnType("jsonb");

        builder.Property(a => a.ResponsePayload)
            .HasColumnName("response_payload")
            .HasColumnType("jsonb");

        builder.Property(a => a.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(a => a.AttemptNumber)
            .HasColumnName("attempt_number")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(a => a.ExecutedAt)
            .HasColumnName("executed_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(a => a.DurationMs)
            .HasColumnName("duration_ms");

        // Action-level idempotency unique constraint: (WebhookEventId, RuleActionId)
        builder.HasIndex(a => new { a.WebhookEventId, a.RuleActionId })
            .IsUnique()
            .HasDatabaseName("uq_action_executions_event_rule_action");

        // Fast lookup index for skipping succeeded actions on retry
        builder.HasIndex(a => new { a.WebhookEventId, a.RuleActionId, a.Status })
            .HasDatabaseName("idx_action_executions_lookup");
    }
}
