using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.DTOs.GitHub;
using GitHubBot.Application.Helpers;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.ActionHandlers;
using Moq;

namespace GitHubBot.UnitTests;

public class GitHubCommentActionHandlerTests
{
    private readonly Mock<IGitHubApiClient> _apiClientMock;
    private readonly Mock<IGitHubTokenProvider> _tokenProviderMock;
    private readonly GitHubCommentActionHandler _handler;
    private readonly ConnectedRepository _repository;
    private readonly WebhookEvent _webhookEvent;

    public GitHubCommentActionHandlerTests()
    {
        _apiClientMock = new Mock<IGitHubApiClient>();
        _tokenProviderMock = new Mock<IGitHubTokenProvider>();
        _handler = new GitHubCommentActionHandler(_apiClientMock.Object, _tokenProviderMock.Object);

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

        _apiClientMock
            .Setup(c => c.GetIssueCommentsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GitHubCommentDto>());
    }

    [Fact]
    public void ActionType_ShouldBe_GithubAddComment()
    {
        _handler.ActionType.Should().Be(ActionType.GithubAddComment);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfigurationIsValid_ShouldAddCommentWithMarker()
    {
        // Arrange (Test 3: AddComment handler success)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddComment,
            Configuration = "{\"body\":\"Thank you for opening this issue!\"}"
        };

        var expectedMarker = CommentIdempotencyHelper.GenerateMarker(_webhookEvent.Id, ruleAction.Id);

        _apiClientMock
            .Setup(c => c.AddCommentAsync(
                "ghp_valid_mock_token",
                "octocat",
                "Hello-World",
                100,
                It.Is<string>(b => b.Contains("Thank you") && b.Contains(expectedMarker)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubCommentDto
            {
                Id = 9999,
                Body = $"Thank you for opening this issue!\n\n{expectedMarker}",
                HtmlUrl = "https://github.com/octocat/Hello-World/issues/100#issuecomment-9999"
            });

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 100,
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.RequestPayload.Should().Contain("Thank you");
        result.ResponsePayload.Should().Contain("9999");

        _apiClientMock.Verify(c => c.AddCommentAsync(
            "ghp_valid_mock_token",
            "octocat",
            "Hello-World",
            100,
            It.Is<string>(b => b.Contains(expectedMarker)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"body\":\"\"}")]
    [InlineData("{\"body\":\"   \"}")]
    [InlineData("{\"other\":\"value\"}")]
    [InlineData("not-json")]
    public async Task ExecuteAsync_WhenConfigurationIsInvalid_ShouldReturnFailedWithoutCallingApi(string config)
    {
        // Arrange (Test 4: AddComment invalid configuration)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddComment,
            Configuration = config
        };

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 100,
            attemptNumber: 1);

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransientError.Should().BeFalse(); // Permanent configuration failure

        _apiClientMock.Verify(c => c.AddCommentAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CommentIdempotencyMarker_ShouldBeDeterministic_ForSameEventAndAction()
    {
        // Arrange (Test 17: Comment idempotency marker is deterministic)
        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var actionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Act
        var marker1 = CommentIdempotencyHelper.GenerateMarker(eventId, actionId);
        var marker2 = CommentIdempotencyHelper.GenerateMarker(eventId, actionId);

        // Assert
        marker1.Should().Be(marker2);
        marker1.Should().Be("<!-- github-bot:event:11111111-1111-1111-1111-111111111111:action:22222222-2222-2222-2222-222222222222 -->");
    }

    [Fact]
    public async Task ExecuteAsync_WhenMarkerAlreadyExistsInComments_ShouldNotCreateDuplicateComment()
    {
        // Arrange (Test 18: Existing comment marker prevents duplicate comment)
        var ruleAction = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddComment,
            Configuration = "{\"body\":\"Automated greeting.\"}"
        };

        var marker = CommentIdempotencyHelper.GenerateMarker(_webhookEvent.Id, ruleAction.Id);

        var existingComments = new List<GitHubCommentDto>
        {
            new() { Id = 101, Body = "User comment" },
            new() { Id = 102, Body = $"Automated greeting.\n\n{marker}" }
        };

        _apiClientMock
            .Setup(c => c.GetIssueCommentsAsync(
                "ghp_valid_mock_token",
                "octocat",
                "Hello-World",
                55,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingComments);

        var context = new ActionContext(
            _webhookEvent,
            ruleAction,
            _repository,
            issueOrPrNumber: 55,
            attemptNumber: 2); // Simulating retry after worker crash

        // Act
        var result = await _handler.ExecuteAsync(context);

        // Assert
        result.Success.Should().BeTrue();
        result.ResponsePayload.Should().Contain("102");
        result.ResponsePayload.Should().Contain("idempotency marker detected");

        // CRITICAL: Must NOT call AddCommentAsync when marker was detected!
        _apiClientMock.Verify(c => c.AddCommentAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
