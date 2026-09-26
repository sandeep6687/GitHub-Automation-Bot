using FluentAssertions;
using GitHubBot.Application.DTOs.Worker;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Infrastructure.BackgroundWorkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GitHubBot.UnitTests;

public class EventProcessingWorkerTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IWebhookEventRepository> _repoMock;
    private readonly Mock<IEventProcessingService> _processingServiceMock;
    private readonly Mock<ILogger<EventProcessingWorker>> _loggerMock;
    private readonly WorkerOptions _options;

    public EventProcessingWorkerTests()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _repoMock = new Mock<IWebhookEventRepository>();
        _processingServiceMock = new Mock<IEventProcessingService>();
        _loggerMock = new Mock<ILogger<EventProcessingWorker>>();

        _options = new WorkerOptions
        {
            Enabled = true,
            BatchSize = 5,
            PollIntervalSeconds = 1,
            LockTimeoutMinutes = 5,
            StaleCheckInterval = TimeSpan.Zero // Force stale recovery to trigger in tests
        };

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);

        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IWebhookEventRepository)))
            .Returns(_repoMock.Object);
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IEventProcessingService)))
            .Returns(_processingServiceMock.Object);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenEventsClaimed_ShouldDispatchToProcessingService()
    {
        // Arrange
        var events = new List<WebhookEvent>
        {
            new() { Id = Guid.NewGuid(), DeliveryId = "deliv-1", Status = EventStatus.Processing },
            new() { Id = Guid.NewGuid(), DeliveryId = "deliv-2", Status = EventStatus.Processing }
        };

        _repoMock
            .Setup(r => r.ClaimBatchAsync(_options.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        _processingServiceMock
            .Setup(s => s.ProcessEventAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventProcessingResult.Success());

        var worker = new EventProcessingWorker(
            _scopeFactoryMock.Object,
            _loggerMock.Object,
            Options.Create(_options));

        // Act
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(2);
        _processingServiceMock.Verify(s => s.ProcessEventAsync(events[0], It.IsAny<CancellationToken>()), Times.Once);
        _processingServiceMock.Verify(s => s.ProcessEventAsync(events[1], It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenNoEventsClaimed_ShouldReturnZero()
    {
        // Arrange
        _repoMock
            .Setup(r => r.ClaimBatchAsync(_options.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WebhookEvent>());

        var worker = new EventProcessingWorker(
            _scopeFactoryMock.Object,
            _loggerMock.Object,
            Options.Create(_options));

        // Act
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(0);
        _processingServiceMock.Verify(s => s.ProcessEventAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBatchAsync_ShouldTriggerStaleClaimRecovery()
    {
        // Arrange
        _repoMock
            .Setup(r => r.RecoverStaleProcessingClaimsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        _repoMock
            .Setup(r => r.ClaimBatchAsync(_options.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WebhookEvent>());

        var worker = new EventProcessingWorker(
            _scopeFactoryMock.Object,
            _loggerMock.Object,
            Options.Create(_options));

        // Act
        await worker.ProcessBatchAsync();

        // Assert
        _repoMock.Verify(r => r.RecoverStaleProcessingClaimsAsync(_options.StaleThreshold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenOneEventThrows_ShouldContinueProcessingNextEvent()
    {
        // Arrange
        var evt1 = new WebhookEvent { Id = Guid.NewGuid(), DeliveryId = "deliv-err", Status = EventStatus.Processing };
        var evt2 = new WebhookEvent { Id = Guid.NewGuid(), DeliveryId = "deliv-ok", Status = EventStatus.Processing };

        _repoMock
            .Setup(r => r.ClaimBatchAsync(_options.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { evt1, evt2 });

        _processingServiceMock
            .Setup(s => s.ProcessEventAsync(evt1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Crash during evt1"));

        _processingServiceMock
            .Setup(s => s.ProcessEventAsync(evt2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventProcessingResult.Success());

        var worker = new EventProcessingWorker(
            _scopeFactoryMock.Object,
            _loggerMock.Object,
            Options.Create(_options));

        // Act
        var processedCount = await worker.ProcessBatchAsync();

        // Assert
        processedCount.Should().Be(2);
        _processingServiceMock.Verify(s => s.ProcessEventAsync(evt2, It.IsAny<CancellationToken>()), Times.Once);
    }
}
