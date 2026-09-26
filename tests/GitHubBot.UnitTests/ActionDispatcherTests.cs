using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace GitHubBot.UnitTests;

public class ActionDispatcherTests
{
    private readonly Mock<IActionHandler> _labelHandlerMock;
    private readonly Mock<IActionHandler> _commentHandlerMock;
    private readonly Mock<IActionExecutionRepository> _actionExecutionRepoMock;
    private readonly Mock<ILogger<ActionDispatcher>> _loggerMock;
    private readonly ActionDispatcher _dispatcher;
    private readonly WebhookEvent _event;
    private readonly ConnectedRepository _repository;

    public ActionDispatcherTests()
    {
        _labelHandlerMock = new Mock<IActionHandler>();
        _labelHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);

        _commentHandlerMock = new Mock<IActionHandler>();
        _commentHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddComment);

        _actionExecutionRepoMock = new Mock<IActionExecutionRepository>();
        _loggerMock = new Mock<ILogger<ActionDispatcher>>();

        _dispatcher = new ActionDispatcher(
            new[] { _labelHandlerMock.Object, _commentHandlerMock.Object },
            _actionExecutionRepoMock.Object,
            _loggerMock.Object);

        _repository = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            Owner = "octocat",
            Name = "Hello-World"
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
    public async Task DispatchActionAsync_ShouldSelectCorrectHandler_ForGivenActionType()
    {
        // Arrange (Test 12: ActionDispatcher selects correct handler)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"bug\"}"
        };

        _labelHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Succeeded("{\"label\":\"bug\"}", "{\"labels\":[\"bug\"]}", 45));

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 123);

        // Assert
        result.Success.Should().BeTrue();
        _labelHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _commentHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Never);

        _actionExecutionRepoMock.Verify(r => r.AddAsync(
            It.Is<ActionExecution>(a => a.ActionType == ActionType.GithubAddLabel && a.Status == ExecutionStatus.Success),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenActionTypeIsUnknown_ShouldRejectSafelyAndRecordFailure()
    {
        // Arrange (Test 13: Unknown ActionType rejected safely)
        // Slack is not supported in Phase 7, so it acts as an unregistered action type
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotification,
            Configuration = "{}"
        };

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 123);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransient.Should().BeFalse(); // Unknown type is non-retryable
        result.ErrorMessage.Should().Contain("Unknown or unsupported action type");

        _actionExecutionRepoMock.Verify(r => r.AddAsync(
            It.Is<ActionExecution>(a => a.Status == ExecutionStatus.Failed && a.ErrorMessage!.Contains("Unknown")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenActionExecutionAlreadySucceeded_ShouldSkipExecution()
    {
        // Arrange (Test 14: Existing successful ActionExecution is skipped)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"bug\"}"
        };

        var existingSuccess = new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = _event.Id,
            RuleActionId = ruleAction.Id,
            ActionType = ActionType.GithubAddLabel,
            Status = ExecutionStatus.Success
        };

        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, ruleAction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSuccess);

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 123);

        // Assert
        result.Success.Should().BeTrue();
        result.WasSkipped.Should().BeTrue();

        // Handler must NEVER be called
        _labelHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenActionPreviouslyFailed_ShouldBeRetried()
    {
        // Arrange (Test 15: Failed action can be retried)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"bug\"}"
        };

        var previouslyFailed = new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = _event.Id,
            RuleActionId = ruleAction.Id,
            ActionType = ActionType.GithubAddLabel,
            Status = ExecutionStatus.Failed,
            AttemptNumber = 1
        };

        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, ruleAction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previouslyFailed);

        _labelHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Succeeded("{\"label\":\"bug\"}", "{\"labels\":[\"bug\"]}", 50));

        _event.AttemptCount = 2; // Second attempt

        // Act
        var result = await _dispatcher.DispatchActionAsync(_event, ruleAction, _repository, 123);

        // Assert
        result.Success.Should().BeTrue();
        result.WasSkipped.Should().BeFalse();

        _labelHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Once);

        _actionExecutionRepoMock.Verify(r => r.UpdateAsync(
            It.Is<ActionExecution>(a => a.Status == ExecutionStatus.Success && a.AttemptNumber == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchActionAsync_WhenAction1SucceededAndAction2Failed_OnRetryAction1IsNotRepeated()
    {
        // Arrange (Test 16: Successful Action 1 is not repeated when Action 2 fails)
        var action1 = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"bug\"}",
            ExecutionOrder = 1
        };

        var action2 = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddComment,
            Configuration = "{\"body\":\"Greeting\"}",
            ExecutionOrder = 2
        };

        // Simulate Action 1 already succeeded in database
        var action1Success = new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = _event.Id,
            RuleActionId = action1.Id,
            ActionType = ActionType.GithubAddLabel,
            Status = ExecutionStatus.Success
        };

        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, action1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(action1Success);

        // Action 2 was NOT yet successful
        _actionExecutionRepoMock
            .Setup(r => r.GetByEventAndActionAsync(_event.Id, action2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActionExecution?)null);

        _commentHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Succeeded("{\"body\":\"Greeting\"}", "{\"commentId\":1}", 60));

        // Act: Retry runs Action 1 then Action 2
        var result1 = await _dispatcher.DispatchActionAsync(_event, action1, _repository, 123);
        var result2 = await _dispatcher.DispatchActionAsync(_event, action2, _repository, 123);

        // Assert
        result1.Success.Should().BeTrue();
        result1.WasSkipped.Should().BeTrue(); // Action 1 was skipped!
        _labelHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Never);

        result2.Success.Should().BeTrue();
        result2.WasSkipped.Should().BeFalse(); // Action 2 was executed!
        _commentHandlerMock.Verify(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
