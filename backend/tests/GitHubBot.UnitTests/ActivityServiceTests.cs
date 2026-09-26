using FluentAssertions;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Moq;
using Xunit;

namespace GitHubBot.UnitTests;

public class ActivityServiceTests
{
    private readonly Mock<IConnectedRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IWebhookEventRepository> _webhookEventRepoMock = new();
    private readonly ActivityService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _repositoryId = Guid.NewGuid();
    private readonly Guid _otherRepositoryId = Guid.NewGuid();

    public ActivityServiceTests()
    {
        _sut = new ActivityService(_repoRepoMock.Object, _webhookEventRepoMock.Object);

        _repoRepoMock.Setup(r => r.GetByIdAsync(_repositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository
            {
                Id = _repositoryId,
                UserId = _userId,
                FullName = "owner/my-repo"
            });

        _repoRepoMock.Setup(r => r.GetByIdAsync(_otherRepositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository
            {
                Id = _otherRepositoryId,
                UserId = _otherUserId,
                FullName = "stranger/their-repo"
            });
    }

    [Fact]
    public async Task Test12_GetActivity_QueryBoundedByLimit()
    {
        // Arrange
        _webhookEventRepoMock.Setup(r => r.GetRecentEventsAsync(
                _repositoryId,
                It.IsAny<int>(),
                It.IsAny<DateTime?>(),
                It.IsAny<EventStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookEvent>());

        // Act - request with limit = 500 (must be clamped to 100 max)
        await _sut.GetActivityAsync(_userId, _repositoryId, limit: 500);

        // Assert - clamped to 100
        _webhookEventRepoMock.Verify(r => r.GetRecentEventsAsync(
            _repositoryId,
            100, // clamped
            It.IsAny<DateTime?>(),
            It.IsAny<EventStatus?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test13_GetActivity_NonOwnedRepository_ThrowsUnauthorizedAccessException()
    {
        // Act
        var act = () => _sut.GetActivityAsync(_userId, _otherRepositoryId);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*not own*");
    }

    [Fact]
    public async Task Test14_GetActivity_FilteringByStatus_PassesParsedStatusToRepo()
    {
        // Arrange
        _webhookEventRepoMock.Setup(r => r.GetRecentEventsAsync(
                _repositoryId,
                50,
                null,
                EventStatus.Success,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookEvent>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    DeliveryId = "del-123",
                    EventType = "issues",
                    Action = "opened",
                    Status = EventStatus.Success,
                    CreatedAt = DateTime.UtcNow,
                    ActionExecutions = new List<ActionExecution>
                    {
                        new()
                        {
                            ActionType = ActionType.AddLabel,
                            Status = ExecutionStatus.Success,
                            ExecutedAt = DateTime.UtcNow
                        }
                    }
                }
            });

        // Act
        var result = await _sut.GetActivityAsync(_userId, _repositoryId, status: "Success");

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Status.Should().Be("Success");
        result.Items[0].Actions.Should().HaveCount(1);
        result.Items[0].Actions[0].ActionType.Should().Be("AddLabel");
    }

    [Fact]
    public async Task Test15_GetActivity_FilteringByEventType_PassesEventTypeToRepo()
    {
        // Arrange
        _webhookEventRepoMock.Setup(r => r.GetRecentEventsAsync(
                _repositoryId,
                50,
                null,
                null,
                "pull_request",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookEvent>());

        // Act
        var result = await _sut.GetActivityAsync(_userId, _repositoryId, eventType: "pull_request");

        // Assert
        result.Items.Should().BeEmpty();
        _webhookEventRepoMock.Verify(r => r.GetRecentEventsAsync(
            _repositoryId,
            50,
            null,
            null,
            "pull_request",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
