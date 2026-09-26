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
        var now = DateTime.UtcNow;

        if (_context.Database.IsNpgsql())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // 1. SELECT ... FOR UPDATE SKIP LOCKED
            // Only select events that are Pending or (Retrying and next_retry_at <= now)
            var sql = @"
                SELECT id AS ""Value""
                FROM webhook_events
                WHERE status IN ('Pending', 'Retrying')
                  AND (next_retry_at IS NULL OR next_retry_at <= {0})
                ORDER BY created_at ASC
                LIMIT {1}
                FOR UPDATE SKIP LOCKED";

            var claimedIds = await _context.Database
                .SqlQueryRaw<Guid>(sql, now, batchSize)
                .ToListAsync(cancellationToken);

            if (claimedIds.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return Array.Empty<WebhookEvent>();
            }

            // 2. UPDATE status = 'Processing', claimed_at = now, attempt_count = attempt_count + 1
            var pNow = new NpgsqlParameter("now", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = now };
            var pIds = new NpgsqlParameter("ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = claimedIds.ToArray() };

            await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE webhook_events
                  SET status = 'Processing',
                      claimed_at = @now,
                      attempt_count = attempt_count + 1,
                      updated_at = @now
                  WHERE id = ANY(@ids)",
                new object[] { pNow, pIds },
                cancellationToken);

            // 3. COMMIT transaction: Row locks are completely released before processing!
            await transaction.CommitAsync(cancellationToken);

            // 4. Load full entities without holding any database locks
            var claimedEvents = await _context.WebhookEvents
                .Include(e => e.Repository)
                .Where(e => claimedIds.Contains(e.Id))
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(cancellationToken);

            return claimedEvents;
        }
        else
        {
            // InMemory / relational fallback for testing environments
            var candidates = await _context.WebhookEvents
                .Include(e => e.Repository)
                .Where(e => (e.Status == EventStatus.Pending || e.Status == EventStatus.Retrying)
                            && (!e.NextRetryAt.HasValue || e.NextRetryAt.Value <= now))
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            foreach (var evt in candidates)
            {
                evt.Status = EventStatus.Processing;
                evt.ClaimedAt = now;
                evt.AttemptCount += 1;
                evt.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return candidates;
        }
    }

    public async Task<int> RecoverStaleProcessingClaimsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(staleThreshold);
        var now = DateTime.UtcNow;

        if (_context.Database.IsNpgsql())
        {
            var pNow = new NpgsqlParameter("now", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = now };
            var pCutoff = new NpgsqlParameter("cutoff", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = cutoff };

            return await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE webhook_events
                  SET status = 'Retrying',
                      next_retry_at = @now,
                      claimed_at = NULL,
                      last_error = 'Worker crash recovery',
                      updated_at = @now
                  WHERE status = 'Processing'
                    AND claimed_at < @cutoff",
                new object[] { pNow, pCutoff },
                cancellationToken);
        }
        else
        {
            var staleEvents = await _context.WebhookEvents
                .Where(e => e.Status == EventStatus.Processing && e.ClaimedAt.HasValue && e.ClaimedAt.Value < cutoff)
                .ToListAsync(cancellationToken);

            foreach (var evt in staleEvents)
            {
                evt.Status = EventStatus.Retrying;
                evt.NextRetryAt = now;
                evt.ClaimedAt = null;
                evt.LastError = "Worker crash recovery";
                evt.UpdatedAt = now;
            }

            return await _context.SaveChangesAsync(cancellationToken);
        }
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
