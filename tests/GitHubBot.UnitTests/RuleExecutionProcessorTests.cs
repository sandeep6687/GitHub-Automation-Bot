using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Domain.Logic;
using Moq;

namespace GitHubBot.UnitTests;

public class RuleExecutionProcessorTests
{
    private readonly Mock<IRuleRepository> _ruleRepoMock;
    private readonly RuleEngine _ruleEngine;
    private readonly RuleExecutionProcessor _processor;
    private readonly Guid _repoId = Guid.NewGuid();

    public RuleExecutionProcessorTests()
    {
        _ruleRepoMock = new Mock<IRuleRepository>();
        _ruleEngine = new RuleEngine();
        _processor = new RuleExecutionProcessor(_ruleRepoMock.Object, _ruleEngine);
    }

    [Fact]
    public async Task ProcessAsync_ShouldLoadRules_Evaluate_AndUpdateParsedData()
    {
        // Arrange
        var rule = new Rule
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repoId,
            Name = "Bug Labeler",
            EventType = "issues.opened",
            IsEnabled = true,
            Conditions = new List<RuleCondition>
            {
                new() { ConditionType = ConditionType.TitleContains, Value = "bug" }
            },
            Actions = new List<RuleAction>
            {
                new() { ActionType = ActionType.GithubAddLabel, ExecutionOrder = 0 },
                new() { ActionType = ActionType.SlackNotification, ExecutionOrder = 1 }
            }
        };

        _ruleRepoMock
            .Setup(r => r.GetActiveRulesForEventAsync(_repoId, "issues.opened", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { rule });

        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repoId,
            EventType = "issues",
            Action = "opened",
            RawPayload = "{\"action\":\"opened\",\"issue\":{\"title\":\"Found a bug in payments\",\"user\":{\"login\":\"dev\"},\"labels\":[]}}"
        };

        // Act
        await _processor.ProcessAsync(evt);

        // Assert
        _ruleRepoMock.Verify(r => r.GetActiveRulesForEventAsync(_repoId, "issues.opened", It.IsAny<CancellationToken>()), Times.Once);

        evt.ParsedData.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(evt.ParsedData!);
        doc.RootElement.GetProperty("matchedRules").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("actionCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoRulesMatch_ShouldRecordZeroMatchedRules()
    {
        // Arrange
        var rule = new Rule
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repoId,
            Name = "Bug Labeler",
            EventType = "issues.opened",
            IsEnabled = true,
            Conditions = new List<RuleCondition>
            {
                new() { ConditionType = ConditionType.TitleContains, Value = "crash" }
            }
        };

        _ruleRepoMock
            .Setup(r => r.GetActiveRulesForEventAsync(_repoId, "issues.opened", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { rule });

        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repoId,
            EventType = "issues",
            Action = "opened",
            RawPayload = "{\"action\":\"opened\",\"issue\":{\"title\":\"Just a feature request\",\"user\":{\"login\":\"dev\"},\"labels\":[]}}"
        };

        // Act
        await _processor.ProcessAsync(evt);

        // Assert
        using var doc = JsonDocument.Parse(evt.ParsedData!);
        doc.RootElement.GetProperty("matchedRules").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("actionCount").GetInt32().Should().Be(0);
    }
}
