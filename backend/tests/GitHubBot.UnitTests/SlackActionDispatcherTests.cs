using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GitHubBot.UnitTests;

public class SlackActionDispatcherTests
{
    private readonly Mock<IActionHandler> _labelHandlerMock;
    private readonly Mock<IActionHandler> _slackHandlerMock;
    private readonly Mock<IActionExecutionRepository> _actionExecutionRepoMock;
    private readonly ActionDispatcher _dispatcher;
    private readonly WebhookEvent _event;
    private readonly ConnectedRepository _repository;

    public SlackActionDispatcherTests()
    {
        _labelHandlerMock = new Mock<IActionHandler>();
        _labelHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);
        _labelHandlerMock.Setup(h => h.CanHandle(ActionType.GithubAddLabel)).Returns(true);

        _slackHandlerMock = new Mock<IActionHandler>();
        _slackHandlerMock.Setup(h => h.ActionType).Returns(ActionType.SlackNotify);
        _slackHandlerMock.Setup(h => h.CanHandle(ActionType.SlackNotify)).Returns(true);
        _slackHandlerMock.Setup(h => h.CanHandle(ActionType.SlackNotification)).Returns(true);

        _actionExecutionRepoMock = new Mock<IActionExecutionRepository>();

        _dispatcher = new ActionDispatcher(
            new[] { _labelHandlerMock.Object, _slackHandlerMock.Object },
            _actionExecutionRepoMock.Object,
            NullLogger<ActionDispatcher>.Instance);

        _repository = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            Owner = "octocat",
            Name = "Hello-World",
            FullName = "octocat/Hello-World"
        };

        _event = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repository.Id,
            EventType = "issues",
            Action = "opened",
            AttemptCount = 1
        };
    }

    [Fact]
    public async Task DispatchActionAsync_ShouldSelectSlackHandler_WhenActionTypeIsSlackNotify()
    {
        // Arrange (Test 23: ActionDispatcher selects Slack handler)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotify,
            Configuration = "{\"message\":\"Bug alert\"}"
        };

        _slackHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Succeeded("{\"text\":\"Bug alert\"}", "{\"status\":\"ok\"}", 50));

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 1);

        // Assert
        result.Success.Should().BeTrue();
        _slackHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _labelHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Never);

        _actionExecutionRepoMock.Verify(r => r.AddAsync(
            It.Is<ActionExecution>(a => a.ActionType == ActionType.SlackNotify && a.Status == ExecutionStatus.Success),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenSlackActionAlreadySucceeded_ShouldSkipExecution()
    {
        // Arrange (Test 24: Existing successful Slack ActionExecution is skipped)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotify,
            Configuration = "{\"message\":\"Hello\"}"
        };

        var existingSuccess = new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = _event.Id,
            RuleActionId = ruleAction.Id,
            ActionType = ActionType.SlackNotify,
            Status = ExecutionStatus.Success
        };

        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, ruleAction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSuccess);

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 1);

        // Assert
        result.Success.Should().BeTrue();
        result.WasSkipped.Should().BeTrue();

        // Handler must NOT be executed
        _slackHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenSlackActionFailedTransiently_CanBeRetried()
    {
        // Arrange (Test 25: Slack failure can retry)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotify,
            Configuration = "{\"message\":\"Hello\"}"
        };

        var existingFailed = new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = _event.Id,
            RuleActionId = ruleAction.Id,
            ActionType = ActionType.SlackNotify,
            Status = ExecutionStatus.Failed,
            AttemptNumber = 1
        };

        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, ruleAction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFailed);

        _slackHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Succeeded("{\"text\":\"Hello\"}", "{\"status\":\"ok\"}", 60));

        _event.AttemptCount = 2; // Retry attempt

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 1);

        // Assert
        result.Success.Should().BeTrue();
        result.WasSkipped.Should().BeFalse();

        _slackHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Once);

        _actionExecutionRepoMock.Verify(r => r.UpdateAsync(
            It.Is<ActionExecution>(a => a.Status == ExecutionStatus.Success && a.AttemptNumber == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenGitHubActionSucceedsAndSlackFails_OnRetryGitHubActionIsNotRepeated()
    {
        // Arrange (Test 26: Successful GitHub action is not repeated when Slack fails)
        var githubAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"bug\"}",
            ExecutionOrder = 1
        };

        var slackAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotify,
            Configuration = "{\"message\":\"Alert\"}",
            ExecutionOrder = 2
        };

        // GitHub action was already recorded as SUCCESS in Postgres
        var githubSuccess = new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = _event.Id,
            RuleActionId = githubAction.Id,
            ActionType = ActionType.GithubAddLabel,
            Status = ExecutionStatus.Success
        };

        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, githubAction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(githubSuccess);

        // Slack action was NOT successful
        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, slackAction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActionExecution?)null);

        _slackHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Succeeded("{\"text\":\"Alert\"}", "{\"status\":\"ok\"}", 40));

        // Act: Dispatch both actions during retry
        var resultGitHub = await _dispatcher.DispatchActionAsync(_event, githubAction, _repository, 10);
        var resultSlack = await _dispatcher.DispatchActionAsync(_event, slackAction, _repository, 10);

        // Assert
        resultGitHub.Success.Should().BeTrue();
        resultGitHub.WasSkipped.Should().BeTrue();
        _labelHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Never);

        resultSlack.Success.Should().BeTrue();
        resultSlack.WasSkipped.Should().BeFalse();
        _slackHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
