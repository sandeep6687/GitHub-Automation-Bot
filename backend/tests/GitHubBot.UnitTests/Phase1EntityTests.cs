using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using FluentAssertions;

namespace GitHubBot.UnitTests;

public class Phase1EntityTests
{
    [Fact]
    public void AllRequiredEntities_CanBeInstantiated()
    {
        var user = new User { Email = "test@example.com" };
        var account = new GithubAccount { UserId = user.Id, GithubUserId = 12345, Login = "octocat" };
        var repo = new ConnectedRepository { UserId = user.Id, GithubRepositoryId = 98765, FullName = "octocat/hello-world" };
        var rule = new Rule { RepositoryId = repo.Id, Name = "Auto Label", EventType = "issues.opened" };
        var condition = new RuleCondition { RuleId = rule.Id, ConditionType = ConditionType.TitleContains, Field = "issue.title", Value = "bug" };
        var action = new RuleAction { RuleId = rule.Id, ActionType = ActionType.GithubAddLabel, Configuration = "{\"label\":\"bug\"}" };
        var webhookEvent = new WebhookEvent
        {
            RepositoryId = repo.Id,
            DeliveryId = "delivery-uuid-1",
            EventType = "issues",
            Status = EventStatus.Pending,
            RawPayload = "{}"
        };
        var execution = new ActionExecution
        {
            WebhookEventId = webhookEvent.Id,
            RuleActionId = action.Id,
            ActionType = ActionType.GithubAddLabel,
            Status = ExecutionStatus.Pending
        };

        user.Id.Should().NotBeEmpty();
        account.UserId.Should().Be(user.Id);
        repo.UserId.Should().Be(user.Id);
        rule.RepositoryId.Should().Be(repo.Id);
        condition.RuleId.Should().Be(rule.Id);
        action.RuleId.Should().Be(rule.Id);
        webhookEvent.RepositoryId.Should().Be(repo.Id);
        execution.WebhookEventId.Should().Be(webhookEvent.Id);
    }

    [Fact]
    public void WebhookEvent_SupportsAllRequiredStatuses()
    {
        Enum.GetNames<EventStatus>().Should().Contain(new[]
        {
            nameof(EventStatus.Pending),
            nameof(EventStatus.Processing),
            nameof(EventStatus.Retrying),
            nameof(EventStatus.Success),
            nameof(EventStatus.Failed)
        });
    }

    [Fact]
    public void WebhookEvent_ContainsAllRetryAndRecoveryFields()
    {
        var evt = new WebhookEvent
        {
            AttemptCount = 2,
            MaxAttempts = 6,
            NextRetryAt = DateTime.UtcNow.AddMinutes(10),
            ClaimedAt = DateTime.UtcNow,
            LastError = "Network timeout",
            ProcessedAt = DateTime.UtcNow
        };

        evt.AttemptCount.Should().Be(2);
        evt.MaxAttempts.Should().Be(6);
        evt.NextRetryAt.Should().NotBeNull();
        evt.ClaimedAt.Should().NotBeNull();
        evt.LastError.Should().Be("Network timeout");
        evt.ProcessedAt.Should().NotBeNull();
    }
}
