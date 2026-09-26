using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.DTOs.Rules;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Moq;
using Xunit;

namespace GitHubBot.UnitTests;

public class RuleServiceTests
{
    private readonly Mock<IConnectedRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IRuleRepository> _ruleRepoMock = new();
    private readonly RuleService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _repositoryId = Guid.NewGuid();
    private readonly Guid _otherRepositoryId = Guid.NewGuid();

    public RuleServiceTests()
    {
        _sut = new RuleService(_repoRepoMock.Object, _ruleRepoMock.Object);

        var ownedRepo = new ConnectedRepository
        {
            Id = _repositoryId,
            UserId = _userId,
            FullName = "owner/my-repo",
            Owner = "owner",
            Name = "my-repo"
        };

        _repoRepoMock.Setup(r => r.GetByIdAsync(_repositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownedRepo);

        _repoRepoMock.Setup(r => r.GetByIdAsync(_otherRepositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectedRepository
            {
                Id = _otherRepositoryId,
                UserId = _otherUserId,
                FullName = "stranger/their-repo"
            });
    }

    [Fact]
    public async Task Test01_GetRules_ForOwnedRepository_ReturnsRules()
    {
        // Arrange
        var rules = new List<Rule>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RepositoryId = _repositoryId,
                Name = "Bug Rule",
                EventType = "issues.opened",
                IsEnabled = true,
                Priority = 1,
                Conditions = new List<RuleCondition>
                {
                    new() { Id = Guid.NewGuid(), ConditionType = ConditionType.TitleContains, Field = "title", Value = "bug" }
                },
                Actions = new List<RuleAction>
                {
                    new() { Id = Guid.NewGuid(), ActionType = ActionType.AddLabel, Configuration = "{\"label\":\"bug\"}" }
                }
            }
        };

        _ruleRepoMock.Setup(r => r.GetByRepositoryIdAsync(_repositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        // Act
        var result = await _sut.GetRulesAsync(_userId, _repositoryId);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Bug Rule");
        result[0].Conditions.Should().HaveCount(1);
        result[0].Actions.Should().HaveCount(1);
        result[0].Actions[0].ActionType.Should().Be("AddLabel");
    }

    [Fact]
    public async Task Test02_GetRules_ForAnotherUsersRepository_ThrowsUnauthorizedAccessException()
    {
        // Act
        var act = () => _sut.GetRulesAsync(_userId, _otherRepositoryId);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*not own*");
    }

    [Fact]
    public async Task Test03_CreateRule_ValidData_PersistsAndReturnsDto()
    {
        // Arrange
        var request = new CreateRuleRequest
        {
            Name = "PR Checker",
            EventType = "pull_request.opened",
            Priority = 2,
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "AuthorEquals", Value = "octocat" }
            },
            Actions = new List<ActionDto>
            {
                new()
                {
                    ActionType = "SlackNotify",
                    ExecutionOrder = 1,
                    Configuration = JsonDocument.Parse("{\"message\":\"PR opened by {{author}}\"}").RootElement
                }
            }
        };

        _ruleRepoMock.Setup(r => r.AddAsync(It.IsAny<Rule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Rule r, CancellationToken _) => r);

        // Act
        var result = await _sut.CreateRuleAsync(_userId, _repositoryId, request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("PR Checker");
        result.EventType.Should().Be("pull_request.opened");
        result.Enabled.Should().BeTrue();
        result.Conditions.Should().HaveCount(1);
        result.Conditions[0].ConditionType.Should().Be("AuthorEquals");
        result.Conditions[0].Field.Should().Be("author");
        result.Actions.Should().HaveCount(1);
        result.Actions[0].ActionType.Should().Be("SlackNotify");

        _ruleRepoMock.Verify(r => r.AddAsync(It.Is<Rule>(rule =>
            rule.RepositoryId == _repositoryId &&
            rule.Name == "PR Checker"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("unknown_event")]
    [InlineData("fork")]
    [InlineData("")]
    public async Task Test04_CreateRule_InvalidEventType_ThrowsArgumentException(string eventType)
    {
        var request = new CreateRuleRequest
        {
            Name = "Invalid Event Rule",
            EventType = eventType,
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"test\"}").RootElement }
            }
        };

        var act = () => _sut.CreateRuleAsync(_userId, _repositoryId, request);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("SqlInjectionCondition")]
    [InlineData("RegexMatch")]
    [InlineData("")]
    public async Task Test05_CreateRule_InvalidConditionType_ThrowsArgumentException(string conditionType)
    {
        var request = new CreateRuleRequest
        {
            Name = "Invalid Condition Rule",
            EventType = "issues.opened",
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = conditionType, Value = "test" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"test\"}").RootElement }
            }
        };

        var act = () => _sut.CreateRuleAsync(_userId, _repositoryId, request);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("ArbitraryCodeExec")]
    [InlineData("SendEmail")]
    [InlineData("")]
    public async Task Test06_CreateRule_InvalidActionType_ThrowsArgumentException(string actionType)
    {
        var request = new CreateRuleRequest
        {
            Name = "Invalid Action Rule",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = actionType, Configuration = JsonDocument.Parse("{\"foo\":\"bar\"}").RootElement }
            }
        };

        var act = () => _sut.CreateRuleAsync(_userId, _repositoryId, request);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Test07_CreateRule_InvalidActionConfiguration_ThrowsArgumentException()
    {
        // AddLabel without 'label'
        var request = new CreateRuleRequest
        {
            Name = "Missing Label Config",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"wrong_field\":\"foo\"}").RootElement }
            }
        };

        var act = () => _sut.CreateRuleAsync(_userId, _repositoryId, request);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*'label'*");

        // SlackNotify without 'message'
        var request2 = new CreateRuleRequest
        {
            Name = "Missing Message Config",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = "SlackNotify", Configuration = JsonDocument.Parse("{\"channel\":\"#general\"}").RootElement }
            }
        };

        var act2 = () => _sut.CreateRuleAsync(_userId, _repositoryId, request2);
        await act2.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*'message'*");
    }

    [Fact]
    public async Task Test08_UpdateRule_ValidData_UpdatesAndReturnsDto()
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var existingRule = new Rule
        {
            Id = ruleId,
            RepositoryId = _repositoryId,
            Name = "Old Name",
            EventType = "issues.opened",
            IsEnabled = true,
            Priority = 0
        };

        _ruleRepoMock.Setup(r => r.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRule);

        var updateRequest = new UpdateRuleRequest
        {
            Name = "Updated Name",
            EventType = "issues.labeled",
            Priority = 5,
            IsEnabled = false,
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "LabelContains", Value = "urgent" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddComment", Configuration = JsonDocument.Parse("{\"body\":\"Looking into this.\" }").RootElement }
            }
        };

        // Act
        var result = await _sut.UpdateRuleAsync(_userId, _repositoryId, ruleId, updateRequest);

        // Assert
        result.Name.Should().Be("Updated Name");
        result.EventType.Should().Be("issues.labeled");
        result.Priority.Should().Be(5);
        result.Enabled.Should().BeFalse();
        result.Conditions.Should().HaveCount(1);
        result.Actions.Should().HaveCount(1);
        result.Actions[0].ActionType.Should().Be("AddComment");

        _ruleRepoMock.Verify(r => r.UpdateAsync(existingRule, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test09_ToggleRule_SwitchesEnabledState()
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var existingRule = new Rule
        {
            Id = ruleId,
            RepositoryId = _repositoryId,
            Name = "Toggle Me",
            IsEnabled = true
        };

        _ruleRepoMock.Setup(r => r.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRule);

        // Act
        var result = await _sut.ToggleRuleAsync(_userId, _repositoryId, ruleId, false);

        // Assert
        result.Enabled.Should().BeFalse();
        existingRule.IsEnabled.Should().BeFalse();
        _ruleRepoMock.Verify(r => r.UpdateAsync(existingRule, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test10_DeleteRule_RemovesRule()
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var existingRule = new Rule
        {
            Id = ruleId,
            RepositoryId = _repositoryId,
            Name = "Delete Me"
        };

        _ruleRepoMock.Setup(r => r.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRule);

        // Act
        await _sut.DeleteRuleAsync(_userId, _repositoryId, ruleId);

        // Assert
        _ruleRepoMock.Verify(r => r.DeleteAsync(ruleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test11_UpdateRule_ForDifferentRepository_ThrowsKeyNotFoundException()
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var ruleOnOtherRepo = new Rule
        {
            Id = ruleId,
            RepositoryId = _otherRepositoryId, // Belongs to different repo!
            Name = "Other Repo Rule"
        };

        _ruleRepoMock.Setup(r => r.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleOnOtherRepo);

        var updateRequest = new UpdateRuleRequest
        {
            Name = "Hijack Rule",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"pwned\"}").RootElement }
            }
        };

        // Act - user tries to edit it via _repositoryId
        var act = () => _sut.UpdateRuleAsync(_userId, _repositoryId, ruleId, updateRequest);

        // Assert - IDOR prevented
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
