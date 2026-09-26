using FluentAssertions;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.Persistence;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.UnitTests;

public class WebhookEventRepositoryWorkerTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task ClaimBatchAsync_ShouldClaimPendingEvents_AndTransitionToProcessing()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var repoId = Guid.NewGuid();
        var repo = new ConnectedRepository
        {
            Id = repoId,
            GithubRepositoryId = 12345,
            FullName = "owner/test-repo",
            EncryptedWebhookSecret = "secret"
        };
        context.ConnectedRepositories.Add(repo);

        var event1 = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-p1",
            EventType = "issues",
            Status = EventStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var event2 = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-p2",
            EventType = "pull_request",
            Status = EventStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow.AddMinutes(-4)
        };
        context.WebhookEvents.AddRange(event1, event2);
        await context.SaveChangesAsync();

        var repository = new WebhookEventRepository(context);

        // Act
        var claimed = await repository.ClaimBatchAsync(10);

        // Assert
        claimed.Should().HaveCount(2);
        claimed.All(e => e.Status == EventStatus.Processing).Should().BeTrue();
        claimed.All(e => e.AttemptCount == 1).Should().BeTrue();
        claimed.All(e => e.ClaimedAt.HasValue).Should().BeTrue();

        // Verify in database
        var dbEvent1 = await context.WebhookEvents.FindAsync(event1.Id);
        dbEvent1!.Status.Should().Be(EventStatus.Processing);
        dbEvent1.AttemptCount.Should().Be(1);
        dbEvent1.ClaimedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ClaimBatchAsync_ShouldClaimRetryingEvents_WhenNextRetryAtIsPastOrNull()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var repoId = Guid.NewGuid();
        context.ConnectedRepositories.Add(new ConnectedRepository
        {
            Id = repoId,
            GithubRepositoryId = 54321,
            FullName = "owner/retry-repo",
            EncryptedWebhookSecret = "secret"
        });

        var pastRetryEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-retry-ready",
            EventType = "issues",
            Status = EventStatus.Retrying,
            NextRetryAt = DateTime.UtcNow.AddSeconds(-10),
            AttemptCount = 1
        };

        context.WebhookEvents.Add(pastRetryEvent);
        await context.SaveChangesAsync();

        var repository = new WebhookEventRepository(context);

        // Act
        var claimed = await repository.ClaimBatchAsync(10);

        // Assert
        claimed.Should().ContainSingle(e => e.Id == pastRetryEvent.Id);
        claimed[0].Status.Should().Be(EventStatus.Processing);
        claimed[0].AttemptCount.Should().Be(2); // Incremented from 1
    }

    [Fact]
    public async Task ClaimBatchAsync_ShouldIgnoreRetryingEvents_WhenNextRetryAtIsInFuture()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var repoId = Guid.NewGuid();
        context.ConnectedRepositories.Add(new ConnectedRepository
        {
            Id = repoId,
            GithubRepositoryId = 67890,
            FullName = "owner/future-repo",
            EncryptedWebhookSecret = "secret"
        });

        var futureRetryEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-future",
            EventType = "issues",
            Status = EventStatus.Retrying,
            NextRetryAt = DateTime.UtcNow.AddMinutes(5), // 5 minutes in future
            AttemptCount = 1
        };

        context.WebhookEvents.Add(futureRetryEvent);
        await context.SaveChangesAsync();

        var repository = new WebhookEventRepository(context);

        // Act
        var claimed = await repository.ClaimBatchAsync(10);

        // Assert
        claimed.Should().BeEmpty();

        var dbEvent = await context.WebhookEvents.FindAsync(futureRetryEvent.Id);
        dbEvent!.Status.Should().Be(EventStatus.Retrying);
        dbEvent.AttemptCount.Should().Be(1); // Not incremented
    }

    [Fact]
    public async Task ClaimBatchAsync_ShouldIgnoreProcessingAndTerminalEvents()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var repoId = Guid.NewGuid();
        context.ConnectedRepositories.Add(new ConnectedRepository
        {
            Id = repoId,
            GithubRepositoryId = 77777,
            FullName = "owner/terminal-repo",
            EncryptedWebhookSecret = "secret"
        });

        var processingEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-proc",
            EventType = "issues",
            Status = EventStatus.Processing,
            ClaimedAt = DateTime.UtcNow
        };
        var successEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-succ",
            EventType = "issues",
            Status = EventStatus.Success
        };
        var failedEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-fail",
            EventType = "issues",
            Status = EventStatus.Failed
        };

        context.WebhookEvents.AddRange(processingEvent, successEvent, failedEvent);
        await context.SaveChangesAsync();

        var repository = new WebhookEventRepository(context);

        // Act
        var claimed = await repository.ClaimBatchAsync(10);

        // Assert
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimBatchAsync_ShouldRespectBatchSize()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var repoId = Guid.NewGuid();
        context.ConnectedRepositories.Add(new ConnectedRepository
        {
            Id = repoId,
            GithubRepositoryId = 88888,
            FullName = "owner/batch-repo",
            EncryptedWebhookSecret = "secret"
        });

        for (int i = 0; i < 5; i++)
        {
            context.WebhookEvents.Add(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = $"deliv-batch-{i}",
                EventType = "issues",
                Status = EventStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10 + i)
            });
        }
        await context.SaveChangesAsync();

        var repository = new WebhookEventRepository(context);

        // Act
        var claimed = await repository.ClaimBatchAsync(3);

        // Assert
        claimed.Should().HaveCount(3);
    }

    [Fact]
    public async Task RecoverStaleProcessingClaimsAsync_ShouldResetOldProcessingClaimsToRetrying()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var repoId = Guid.NewGuid();
        context.ConnectedRepositories.Add(new ConnectedRepository
        {
            Id = repoId,
            GithubRepositoryId = 99999,
            FullName = "owner/stale-repo",
            EncryptedWebhookSecret = "secret"
        });

        var staleEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-stale",
            EventType = "issues",
            Status = EventStatus.Processing,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10), // 10 minutes ago (> 5m threshold)
            AttemptCount = 1
        };

        var freshEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            DeliveryId = "deliv-fresh",
            EventType = "issues",
            Status = EventStatus.Processing,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-1), // 1 minute ago (< 5m threshold)
            AttemptCount = 1
        };

        context.WebhookEvents.AddRange(staleEvent, freshEvent);
        await context.SaveChangesAsync();

        var repository = new WebhookEventRepository(context);

        // Act
        var recovered = await repository.RecoverStaleProcessingClaimsAsync(TimeSpan.FromMinutes(5));

        // Assert
        recovered.Should().Be(1);

        var dbStale = await context.WebhookEvents.FindAsync(staleEvent.Id);
        dbStale!.Status.Should().Be(EventStatus.Retrying);
        dbStale.ClaimedAt.Should().BeNull();
        dbStale.NextRetryAt.Should().NotBeNull();
        dbStale.NextRetryAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        dbStale.LastError.Should().Be("Worker crash recovery");

        var dbFresh = await context.WebhookEvents.FindAsync(freshEvent.Id);
        dbFresh!.Status.Should().Be(EventStatus.Processing);
        dbFresh.ClaimedAt.Should().NotBeNull();
    }
}
