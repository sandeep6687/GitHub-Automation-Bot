using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.ActionHandlers;
using Moq;

namespace GitHubBot.UnitTests;

public class GitHubLabelActionHandlerTests
{
    private readonly Mock<IGitHubApiClient> _apiClientMock;
    private readonly Mock<IGitHubTokenProvider> _tokenProviderMock;
    private readonly GitHubLabelActionHandler _handler;
    private readonly ConnectedRepository _repository;
    private readonly WebhookEvent _webhookEvent;

    public GitHubLabelActionHandlerTests()
    {
        _apiClientMock = new Mock<IGitHubApiClient>();
        _tokenProviderMock = new Mock<IGitHubTokenProvider>();
        _handler = new GitHubLabelActionHandler(_apiClientMock.Object, _tokenProviderMock.Object);

        _repository = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            Owner = "octocat",
            Name = "Hello-World"
        };

        _webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repository.Id,
            EventType = "issues",
            Action = "opened"
        };

        _tokenProviderMock
            .Setup(t => t.GetTokenForRepositoryAsync(_repository.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ghp_valid_mock_token");
    }

    [Fact]
    public void ActionType_ShouldBe_GithubAddLabel()
    {
        _handler.ActionType.Should().Be(ActionType.GithubAddLabel);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfigurationIsValid_ShouldCallGitHubApiAndReturnSuccess()
    {
        // Arrange (Test 1: AddLabel handler success)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"triage:bug\"}"
        };

        _apiClientMock
            .Setup(c => c.AddLabelsAsync(
                "ghp_valid_mock_token",
                "octocat",
                "Hello-World",
                42,
                It.Is<IReadOnlyList<string>>(l => l.Contains("triage:bug")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "triage:bug" });

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 42,
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.RequestPayload.Should().Contain("triage:bug");
        result.ResponsePayload.Should().Contain("triage:bug");
        result.DurationMs.Should().BeGreaterThanOrEqualTo(0);

        _apiClientMock.Verify(c => c.AddLabelsAsync(
            "ghp_valid_mock_token",
            "octocat",
            "Hello-World",
            42,
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"other\":\"property\"}")]
    [InlineData("{\"label\":\"\"}")]
    [InlineData("{\"label\":\"   \"}")]
    [InlineData("invalid-json")]
    public async Task ExecuteAsync_WhenConfigurationIsInvalid_ShouldReturnFailedWithoutCallingApi(string config)
    {
        // Arrange (Test 2: AddLabel invalid configuration)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = config
        };

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 42,
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.IsTransientError.Should().BeFalse(); // Permanent configuration failure

        _apiClientMock.Verify(c => c.AddLabelsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIssueOrPrNumberIsMissing_ShouldReturnPermanentFailure()
    {
        // Arrange
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            Configuration = "{\"label\":\"bug\"}"
        };

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: null, // Missing number
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse();
        result.ErrorMessage.Should().Contain("valid issue or PR number");
    }
}
