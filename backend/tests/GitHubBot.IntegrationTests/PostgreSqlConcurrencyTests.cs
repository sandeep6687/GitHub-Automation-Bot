using FluentAssertions;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.IntegrationTests;

[Collection("PostgreSqlTests")]
public class PostgreSqlConcurrencyTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await PostgresTestHelper.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> SeedRepositoryAsync()
    {
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(987654, "concurrency-test/repo");
        return repoId;
    }

    [Fact]
    public async Task SkipLocked_ConcurrentWorkers_ClaimDistinctEvents_WithZeroOverlap()
    {
        // Arrange: Seed 20 pending events into real PostgreSQL
        var repoId = await SeedRepositoryAsync();
        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            for (int i = 0; i < 20; i++)
            {
                seedContext.WebhookEvents.Add(new WebhookEvent
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repoId,
                    DeliveryId = $"deliv-concurrent-{i}",
                    EventType = "issues",
                    Status = EventStatus.Pending,
                    AttemptCount = 0,
                    CreatedAt = DateTime.UtcNow.AddSeconds(-20 + i)
                });
            }
            await seedContext.SaveChangesAsync();
        }

        // Act: Run two worker claims concurrently on two separate PostgreSQL connections
        var task1 = Task.Run(async () =>
        {
            await using var context1 = PostgresTestHelper.CreateDbContext();
            var repo1 = new WebhookEventRepository(context1);
            return await repo1.ClaimBatchAsync(10);
        });

        var task2 = Task.Run(async () =>
        {
            await using var context2 = PostgresTestHelper.CreateDbContext();
            var repo2 = new WebhookEventRepository(context2);
            return await repo2.ClaimBatchAsync(10);
        });

        var results = await Task.WhenAll(task1, task2);
        var batch1 = results[0];
        var batch2 = results[1];

        // Assert: Real PostgreSQL SKIP LOCKED guarantees exactly 10 distinct events per worker
        batch1.Should().HaveCount(10);
        batch2.Should().HaveCount(10);

        var batch1Ids = batch1.Select(e => e.Id).ToHashSet();
        var batch2Ids = batch2.Select(e => e.Id).ToHashSet();

        // 1. Zero overlap between the two concurrent workers
        batch1Ids.Intersect(batch2Ids).Should().BeEmpty("PostgreSQL FOR UPDATE SKIP LOCKED must prevent duplicate claims");

        // 2. Together they claimed all 20 events
        batch1Ids.Union(batch2Ids).Should().HaveCount(20);

        // 3. Verify real PostgreSQL DB state
        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var allEvents = await verifyContext.WebhookEvents.ToListAsync();
            allEvents.Should().HaveCount(20);
            allEvents.All(e => e.Status == EventStatus.Processing).Should().BeTrue();
            allEvents.All(e => e.AttemptCount == 1).Should().BeTrue();
            allEvents.All(e => e.ClaimedAt.HasValue).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SkipLocked_CommitReleasesLock_AllowingImmediateConcurrentReadsAndUpdates()
    {
        // Arrange
        var repoId = await SeedRepositoryAsync();
        var eventId = Guid.NewGuid();
        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            seedContext.WebhookEvents.Add(new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-lock-release",
                EventType = "issues",
                Status = EventStatus.Pending,
                AttemptCount = 0
            });
            await seedContext.SaveChangesAsync();
        }

        // Act: Worker claims the event
        await using (var claimContext = PostgresTestHelper.CreateDbContext())
        {
            var repo = new WebhookEventRepository(claimContext);
            var claimed = await repo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();
        } // claim transaction committed and connection disposed

        // Assert: A second connection can immediately update the row without waiting or lock contention
        await using (var updateContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await updateContext.WebhookEvents.FindAsync(eventId);
            evt!.Status.Should().Be(EventStatus.Processing);

            // Mutation succeeds immediately with zero lock blocking
            evt.Status = EventStatus.Success;
            evt.ProcessedAt = DateTime.UtcNow;
            await updateContext.SaveChangesAsync();

            var verified = await updateContext.WebhookEvents.FindAsync(eventId);
            verified!.Status.Should().Be(EventStatus.Success);
        }
    }

    [Fact]
    public async Task PostgreSql_StaleClaimRecovery_ResetsOldProcessingEventsToRetrying()
    {
        // Arrange
        var repoId = await SeedRepositoryAsync();
        var staleEventId = Guid.NewGuid();
        var freshEventId = Guid.NewGuid();

        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            seedContext.WebhookEvents.AddRange(
                new WebhookEvent
                {
                    Id = staleEventId,
                    RepositoryId = repoId,
                    DeliveryId = "deliv-stale-pg",
                    EventType = "issues",
                    Status = EventStatus.Processing,
                    ClaimedAt = DateTime.UtcNow.AddMinutes(-10), // 10 minutes ago
                    AttemptCount = 1
                },
                new WebhookEvent
                {
                    Id = freshEventId,
                    RepositoryId = repoId,
                    DeliveryId = "deliv-fresh-pg",
                    EventType = "issues",
                    Status = EventStatus.Processing,
                    ClaimedAt = DateTime.UtcNow.AddMinutes(-1), // 1 minute ago
                    AttemptCount = 1
                });
            await seedContext.SaveChangesAsync();
        }

        // Act: Run real PostgreSQL recovery with 5 minute threshold
        await using var context = PostgresTestHelper.CreateDbContext();
        var repo = new WebhookEventRepository(context);
        var recoveredCount = await repo.RecoverStaleProcessingClaimsAsync(TimeSpan.FromMinutes(5));

        // Assert
        recoveredCount.Should().Be(1);

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var dbStale = await verifyContext.WebhookEvents.FindAsync(staleEventId);
        dbStale!.Status.Should().Be(EventStatus.Retrying);
        dbStale.ClaimedAt.Should().BeNull();
        dbStale.NextRetryAt.Should().NotBeNull();
        dbStale.NextRetryAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        dbStale.LastError.Should().Be("Worker crash recovery");

        var dbFresh = await verifyContext.WebhookEvents.FindAsync(freshEventId);
        dbFresh!.Status.Should().Be(EventStatus.Processing);
        dbFresh.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PostgreSql_ClaimBatch_SuppressesFutureRetries_UntilDue()
    {
        // Arrange
        var repoId = await SeedRepositoryAsync();
        var futureEventId = Guid.NewGuid();
        var pastEventId = Guid.NewGuid();

        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            seedContext.WebhookEvents.AddRange(
                new WebhookEvent
                {
                    Id = futureEventId,
                    RepositoryId = repoId,
                    DeliveryId = "deliv-future-retry",
                    EventType = "issues",
                    Status = EventStatus.Retrying,
                    NextRetryAt = DateTime.UtcNow.AddMinutes(15), // Future
                    AttemptCount = 1
                },
                new WebhookEvent
                {
                    Id = pastEventId,
                    RepositoryId = repoId,
                    DeliveryId = "deliv-past-retry",
                    EventType = "issues",
                    Status = EventStatus.Retrying,
                    NextRetryAt = DateTime.UtcNow.AddSeconds(-30), // Due now
                    AttemptCount = 1
                });
            await seedContext.SaveChangesAsync();
        }

        // Act
        await using var context = PostgresTestHelper.CreateDbContext();
        var repo = new WebhookEventRepository(context);
        var claimed = await repo.ClaimBatchAsync(10);

        // Assert: Only the due event is claimed
        claimed.Should().ContainSingle(e => e.Id == pastEventId);
        claimed[0].AttemptCount.Should().Be(2);

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var dbFuture = await verifyContext.WebhookEvents.FindAsync(futureEventId);
        dbFuture!.Status.Should().Be(EventStatus.Retrying);
        dbFuture.AttemptCount.Should().Be(1); // Not claimed or incremented
    }
}
