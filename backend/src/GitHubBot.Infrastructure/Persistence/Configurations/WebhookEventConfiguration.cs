using GitHubBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GitHubBot.Infrastructure.Persistence.Configurations;

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // RepositoryId is NOT NULL per architecture review
        builder.Property(e => e.RepositoryId)
            .HasColumnName("repository_id")
            .IsRequired();

        builder.Property(e => e.DeliveryId)
            .HasColumnName("delivery_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Action)
            .HasColumnName("action")
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.RawPayload)
            .HasColumnName("raw_payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ParsedData)
            .HasColumnName("parsed_data")
            .HasColumnType("jsonb");

        // Retry & claim recovery fields
        builder.Property(e => e.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.MaxAttempts)
            .HasColumnName("max_attempts")
            .HasDefaultValue(6)
            .IsRequired();

        builder.Property(e => e.NextRetryAt)
            .HasColumnName("next_retry_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.LastError)
            .HasColumnName("last_error");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // 1. Webhook ingestion idempotency index
        builder.HasIndex(e => e.DeliveryId)
            .IsUnique()
            .HasDatabaseName("uq_webhook_events_delivery_id");

        // 2. Worker polling index (claim candidates)
        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("idx_webhook_events_claimable")
            .HasFilter("status IN ('Pending', 'Retrying')");

        // 3. Stale claim recovery index
        builder.HasIndex(e => e.ClaimedAt)
            .HasDatabaseName("idx_webhook_events_stale_claims")
            .HasFilter("status = 'Processing'");

        builder.HasMany(e => e.ActionExecutions)
            .WithOne(a => a.WebhookEvent)
            .HasForeignKey(a => a.WebhookEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
