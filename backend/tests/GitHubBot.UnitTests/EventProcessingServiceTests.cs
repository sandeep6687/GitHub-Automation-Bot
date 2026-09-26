using FluentAssertions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Moq;

namespace GitHubBot.UnitTests;

public class EventProcessingServiceTests
{
    private readonly Mock<IWebhookEventRepository> _repoMock;
    private readonly Mock<IEventProcessor> _processorMock;
    private readonly EventProcessingService _service;

    public EventProcessingServiceTests()
    {
        _repoMock = new Mock<IWebhookEventRepository>();
        _processorMock = new Mock<IEventProcessor>();
        _service = new EventProcessingService(_repoMock.Object, _processorMock.Object);
    }

    [Fact]
    public async Task ProcessEvent_SuccessfulExecution_ShouldTransitionToSuccess()
    {
        // Arrange
        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            DeliveryId = "deliv-101",
            EventType = "issues",
            Status = EventStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 6
        };

        _processorMock
            .Setup(p => p.ProcessAsync(evt, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ProcessEventAsync(evt);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.FinalStatus.Should().Be(EventStatus.Success);
        evt.Status.Should().Be(EventStatus.Success);
        evt.ProcessedAt.Should().NotBeNull();
        evt.ProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        evt.LastError.Should().BeNull();

        _repoMock.Verify(r => r.UpdateAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEvent_FailureWithRemainingAttempts_ShouldTransitionToRetrying()
    {
        // Arrange
        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            DeliveryId = "deliv-102",
            EventType = "issues",
            Status = EventStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 6
        };

        _processorMock
            .Setup(p => p.ProcessAsync(evt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated transient failure"));

        // Act
        var result = await _service.ProcessEventAsync(evt);

        // Assert
        result.Retrying.Should().BeTrue();
        result.FinalStatus.Should().Be(EventStatus.Retrying);
        evt.Status.Should().Be(EventStatus.Retrying);
        evt.NextRetryAt.Should().NotBeNull();
        evt.NextRetryAt.Should().BeAfter(DateTime.UtcNow);
        evt.LastError.Should().Be("Simulated transient failure");
        evt.ProcessedAt.Should().BeNull();

        _repoMock.Verify(r => r.UpdateAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEvent_FailureWithExhaustedAttempts_ShouldTransitionToFailed()
    {
        // Arrange (AttemptCount equals MaxAttempts)
        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            DeliveryId = "deliv-103",
            EventType = "issues",
            Status = EventStatus.Processing,
            AttemptCount = 6,
            MaxAttempts = 6
        };

        _processorMock
            .Setup(p => p.ProcessAsync(evt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fatal unrecoverable error"));

        // Act
        var result = await _service.ProcessEventAsync(evt);

        // Assert
        result.Failed.Should().BeTrue();
        result.FinalStatus.Should().Be(EventStatus.Failed);
        evt.Status.Should().Be(EventStatus.Failed);
        evt.ProcessedAt.Should().NotBeNull();
        evt.ProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        evt.LastError.Should().Be("Fatal unrecoverable error");

        _repoMock.Verify(r => r.UpdateAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEventById_WhenEventNotFound_ShouldReturnNull()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookEvent?)null);

        // Act
        var result = await _service.ProcessEventByIdAsync(missingId);

        // Assert
        result.Should().BeNull();
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DefaultEventProcessor_ShouldCompleteWithoutThrowing()
    {
        // Arrange
        var processor = new DefaultEventProcessor();
        var evt = new WebhookEvent { Id = Guid.NewGuid() };

        // Act
        var act = () => processor.ProcessAsync(evt);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
