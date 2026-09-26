using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.ActionHandlers;
using Moq;

namespace GitHubBot.UnitTests;

public class SlackNotificationActionHandlerTests
{
    private readonly Mock<ISlackApiClient> _slackApiClientMock;
    private readonly SlackNotificationActionHandler _handler;
    private readonly ConnectedRepository _repository;
    private readonly WebhookEvent _webhookEvent;

    public SlackNotificationActionHandlerTests()
    {
        _slackApiClientMock = new Mock<ISlackApiClient>();
        _handler = new SlackNotificationActionHandler(_slackApiClientMock.Object);

        _repository = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            Owner = "octocat",
            Name = "Hello-World",
            FullName = "octocat/Hello-World"
        };

        _webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repository.Id,
            EventType = "issues",
            Action = "opened",
            RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":99,\"title\":\"Server 500 error\",\"user\":{\"login\":\"alice\"},\"html_url\":\"https://github.com/octocat/Hello-World/issues/99\"}}"
        };
    }

    [Fact]
    public void ActionType_ShouldSupport_SlackNotifyAndSlackNotification()
    {
        _handler.ActionType.Should().Be(ActionType.SlackNotify);
        _handler.CanHandle(ActionType.SlackNotify).Should().BeTrue();
        _handler.CanHandle(ActionType.SlackNotification).Should().BeTrue();
        _handler.CanHandle(ActionType.GithubAddLabel).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidConfiguration_ShouldSendRenderedMessageAndReturnSuccess()
    {
        // Arrange (Test 1: Slack handler valid configuration & Test 3: Slack payload contains rendered message)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotify,
            Configuration = "{\"message\":\"Alert in {{repository}}: {{title}} by {{author}} #{{issueNumber}} — {{url}}\"}"
        };

        string capturedMessage = string.Empty;
        _slackApiClientMock
            .Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 99,
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.RequestPayload.Should().Contain("Alert in octocat/Hello-World: Server 500 error by alice #99");
        result.ResponsePayload.Should().Contain("\"status\":\"ok\"");

        capturedMessage.Should().Be("Alert in octocat/Hello-World: Server 500 error by alice #99 — https://github.com/octocat/Hello-World/issues/99");
        _slackApiClientMock.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"channel\":\"#alerts\"}")]
    [InlineData("{\"message\":\"\"}")]
    [InlineData("{\"message\":\"   \"}")]
    [InlineData("invalid-json")]
    public async Task ExecuteAsync_WithInvalidConfiguration_ShouldReturnPermanentFailure(string config)
    {
        // Arrange (Test 2: Slack handler invalid/missing message)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotify,
            Configuration = config
        };

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 99,
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse(); // Permanent configuration failure
        result.ErrorMessage.Should().NotBeNullOrEmpty();

        _slackApiClientMock.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
