using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GitHubBot.Infrastructure.Persistence.Repositories;

public class WebhookEventRepository : IWebhookEventRepository
{
    private readonly AppDbContext _context;

    public WebhookEventRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<WebhookEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.WebhookEvents
            .Include(e => e.Repository)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<WebhookEvent?> GetByDeliveryIdAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        return await _context.WebhookEvents
            .FirstOrDefaultAsync(e => e.DeliveryId == deliveryId, cancellationToken);
    }

    public async Task<bool> TryInsertAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        // 1. Fast path check
        var exists = await _context.WebhookEvents
            .AnyAsync(e => e.DeliveryId == webhookEvent.DeliveryId, cancellationToken);

        if (exists)
        {
            return false;
        }

        try
        {
            await _context.WebhookEvents.AddAsync(webhookEvent, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent race condition caught by the database UNIQUE index
            _context.Entry(webhookEvent).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return true;
        }

        // Generic fallback for relational providers / in-memory test harnesses
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("23505");
    }

    public async Task<IReadOnlyList<WebhookEvent>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        // Will be utilized in Phase 5 background worker claim/release pattern
        return await _context.WebhookEvents
            .Where(e => e.Status == EventStatus.Pending || e.Status == EventStatus.Retrying)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RecoverStaleProcessingClaimsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(staleThreshold);
        var staleEvents = await _context.WebhookEvents
            .Where(e => e.Status == EventStatus.Processing && e.ClaimedAt.HasValue && e.ClaimedAt.Value < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var evt in staleEvents)
        {
            evt.Status = EventStatus.Retrying;
            evt.ClaimedAt = null;
            evt.UpdatedAt = DateTime.UtcNow;
        }

        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        webhookEvent.UpdatedAt = DateTime.UtcNow;
        _context.WebhookEvents.Update(webhookEvent);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        Guid id,
        EventStatus status,
        string? lastError = null,
        DateTime? nextRetryAt = null,
        CancellationToken cancellationToken = default)
    {
        var evt = await _context.WebhookEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (evt != null)
        {
            evt.Status = status;
            evt.LastError = lastError;
            evt.NextRetryAt = nextRetryAt;
            evt.UpdatedAt = DateTime.UtcNow;
            if (status == EventStatus.Success || status == EventStatus.Failed)
            {
                evt.ProcessedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
