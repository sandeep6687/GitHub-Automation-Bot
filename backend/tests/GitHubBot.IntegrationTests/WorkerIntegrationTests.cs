using FluentAssertions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Infrastructure.BackgroundWorkers;
using GitHubBot.Infrastructure.Persistence;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GitHubBot.IntegrationTests;

[Collection("PostgreSqlTests")]
public class WorkerIntegrationTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await PostgresTestHelper.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ServiceProvider BuildServiceProvider(IEventProcessor? customProcessor = null)
    {
        var services = new ServiceCollection();

        services.AddScoped<AppDbContext>(_ => PostgresTestHelper.CreateDbContext());
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();

        if (customProcessor != null)
        {
            services.AddSingleton(customProcessor);
        }
        else
        {
            services.AddScoped<IEventProcessor, DefaultEventProcessor>();
        }

        services.AddScoped<IEventProcessingService, EventProcessingService>();

        services.Configure<WorkerOptions>(options =>
        {
            options.Enabled = true;
            options.BatchSize = 10;
            options.PollIntervalSeconds = 1;
            options.LockTimeoutMinutes = 5;
            options.StaleCheckInterval = TimeSpan.Zero; // Immediate stale check for testing
        });

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Worker_ShouldProcessPendingEvent_ToSuccess()
    {
        // Arrange
        var provider = BuildServiceProvider();
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(1111, "test-owner/test-repo-1");

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            context.WebhookEvents.Add(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = "worker-deliv-1",
                EventType = "issues",
                Status = EventStatus.Pending,
                AttemptCount = 0
            });

            await context.SaveChangesAsync();
        }

        var worker = new EventProcessingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventProcessingWorker>.Instance,
            provider.GetRequiredService<IOptions<WorkerOptions>>());

        // Act - Run a worker cycle
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(1);

        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext.WebhookEvents.SingleAsync(e => e.DeliveryId == "worker-deliv-1");
            evt.Status.Should().Be(EventStatus.Success);
            evt.AttemptCount.Should().Be(1);
            evt.ClaimedAt.Should().NotBeNull();
            evt.ProcessedAt.Should().NotBeNull();
            evt.LastError.Should().BeNull();
        }
    }

    [Fact]
    public async Task Worker_ShouldRetryOnFailure_AndSetNextRetryAt()
    {
        // Arrange - Inject failing processor
        var mockProcessor = new Mock<IEventProcessor>();
        mockProcessor
            .Setup(p => p.ProcessAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("GitHub API connection timeout"));

        var provider = BuildServiceProvider(mockProcessor.Object);
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(2222, "test-owner/test-repo-2");

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            context.WebhookEvents.Add(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = "worker-retry-deliv",
                EventType = "issues",
                Status = EventStatus.Pending,
                AttemptCount = 0,
                MaxAttempts = 6
            });

            await context.SaveChangesAsync();
        }

        var worker = new EventProcessingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventProcessingWorker>.Instance,
            provider.GetRequiredService<IOptions<WorkerOptions>>());

        // Act
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(1);

        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext.WebhookEvents.SingleAsync(e => e.DeliveryId == "worker-retry-deliv");
            evt.Status.Should().Be(EventStatus.Retrying);
            evt.AttemptCount.Should().Be(1);
            evt.NextRetryAt.Should().NotBeNull();
            evt.NextRetryAt.Should().BeAfter(DateTime.UtcNow);
            evt.LastError.Should().Contain("GitHub API connection timeout");
            evt.ProcessedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Worker_ShouldExhaustRetries_AndTransitionToFailed()
    {
        // Arrange - Event at attempt 5 of 6, fails again
        var mockProcessor = new Mock<IEventProcessor>();
        mockProcessor
            .Setup(p => p.ProcessAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fatal error"));

        var provider = BuildServiceProvider(mockProcessor.Object);
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(3333, "test-owner/test-repo-3");

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            context.WebhookEvents.Add(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = "worker-exhaust-deliv",
                EventType = "issues",
                Status = EventStatus.Retrying,
                AttemptCount = 5, // Next claim makes it 6
                MaxAttempts = 6,
                NextRetryAt = DateTime.UtcNow.AddSeconds(-5)
            });

            await context.SaveChangesAsync();
        }

        var worker = new EventProcessingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventProcessingWorker>.Instance,
            provider.GetRequiredService<IOptions<WorkerOptions>>());

        // Act
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(1);

        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext.WebhookEvents.SingleAsync(e => e.DeliveryId == "worker-exhaust-deliv");
            evt.Status.Should().Be(EventStatus.Failed);
            evt.AttemptCount.Should().Be(6); // Exhausted
            evt.LastError.Should().Be("Fatal error");
            evt.ProcessedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Worker_ShouldRecoverStaleProcessingEvent_AndResetToRetrying()
    {
        // Arrange - Event stuck in Processing for 10 minutes (worker crashed)
        var provider = BuildServiceProvider();
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(4444, "test-owner/test-repo-4");

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            context.WebhookEvents.Add(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = "worker-stale-deliv",
                EventType = "issues",
                Status = EventStatus.Processing,
                ClaimedAt = DateTime.UtcNow.AddMinutes(-10),
                AttemptCount = 1
            });

            await context.SaveChangesAsync();
        }

        var worker = new EventProcessingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventProcessingWorker>.Instance,
            provider.GetRequiredService<IOptions<WorkerOptions>>());

        // Act - Running worker cycle triggers stale recovery, then claims recovered event and processes it to Success
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(1);

        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext.WebhookEvents.SingleAsync(e => e.DeliveryId == "worker-stale-deliv");
            evt.Status.Should().Be(EventStatus.Success);
            evt.AttemptCount.Should().Be(2); // Stale attempt 1 + recovered claim attempt 2
        }
    }
}
