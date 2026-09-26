using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Logic;
using GitHubBot.Infrastructure.Persistence;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.IntegrationTests;

[Collection("PostgreSqlTests")]
public class RuleEngineIntegrationTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await PostgresTestHelper.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RuleEngine_PostgresIntegration_ShouldMatchPersistedRuleAndReturnActions()
    {
        // Arrange
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(5555, "rule-test/repo-1");
        var (_, otherRepoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(6666, "rule-test/repo-2");

        var matchingRuleId = Guid.NewGuid();
        var nonMatchingRuleId = Guid.NewGuid();
        var disabledRuleId = Guid.NewGuid();
        var otherRepoRuleId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            // 1. Matching rule (Priority 1)
            var matchingRule = new Rule
            {
                Id = matchingRuleId,
                RepositoryId = repoId,
                Name = "Bug Auto-Labeler",
                EventType = "issues.opened",
                IsEnabled = true,
                Priority = 1,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "bug", CaseSensitive = false }
                },
                Actions = new List<RuleAction>
                {
                    new() { ActionType = ActionType.GithubAddLabel, ExecutionOrder = 0, Configuration = "{\"label\":\"bug\"}" },
                    new() { ActionType = ActionType.SlackNotification, ExecutionOrder = 1, Configuration = "{\"channel\":\"#alerts\"}" }
                }
            };

            // 2. Non-matching rule (condition fails)
            var nonMatchingRule = new Rule
            {
                Id = nonMatchingRuleId,
                RepositoryId = repoId,
                Name = "Crash Responder",
                EventType = "issues.opened",
                IsEnabled = true,
                Priority = 2,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "crash", CaseSensitive = false }
                }
            };

            // 3. Disabled rule (would match condition, but IsEnabled = false)
            var disabledRule = new Rule
            {
                Id = disabledRuleId,
                RepositoryId = repoId,
                Name = "Disabled Bug Rule",
                EventType = "issues.opened",
                IsEnabled = false,
                Priority = 0,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "bug" }
                }
            };

            // 4. Other repository's rule (belongs to otherRepoId)
            var otherRepoRule = new Rule
            {
                Id = otherRepoRuleId,
                RepositoryId = otherRepoId,
                Name = "Other Repo Rule",
                EventType = "issues.opened",
                IsEnabled = true,
                Priority = 0,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "bug" }
                }
            };

            context.Rules.AddRange(matchingRule, nonMatchingRule, disabledRule, otherRepoRule);

            // Insert webhook event for repoId
            context.WebhookEvents.Add(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = "rule-deliv-1",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"title\":\"Found a major bug in payments\",\"user\":{\"login\":\"dev1\"},\"labels\":[]}}"
            });

            await context.SaveChangesAsync();
        }

        // Act: Run RuleExecutionProcessor through EventProcessingService against real PostgreSQL
        await using var testContext = PostgresTestHelper.CreateDbContext();
        var ruleRepo = new RuleRepository(testContext);
        var eventRepo = new WebhookEventRepository(testContext);
        var ruleEngine = new RuleEngine();
        var processor = new RuleExecutionProcessor(ruleRepo, ruleEngine);
        var eventProcessingService = new EventProcessingService(eventRepo, processor);

        var claimedEvents = await eventRepo.ClaimBatchAsync(10);
        claimedEvents.Should().ContainSingle();

        var result = await eventProcessingService.ProcessEventAsync(claimedEvents[0]);

        // Assert
        result.Succeeded.Should().BeTrue();

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var processedEvent = await verifyContext.WebhookEvents.SingleAsync(e => e.DeliveryId == "rule-deliv-1");
        processedEvent.Status.Should().Be(EventStatus.Success);
        processedEvent.ParsedData.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(processedEvent.ParsedData!);
        doc.RootElement.GetProperty("matchedRules").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("actionCount").GetInt32().Should().Be(2);

        var matchedRuleIds = doc.RootElement.GetProperty("matchedRuleIds").EnumerateArray()
            .Select(e => e.GetGuid())
            .ToList();

        matchedRuleIds.Should().ContainSingle(id => id == matchingRuleId);
        matchedRuleIds.Should().NotContain(nonMatchingRuleId);
        matchedRuleIds.Should().NotContain(disabledRuleId);
        matchedRuleIds.Should().NotContain(otherRepoRuleId);
    }

    [Fact]
    public async Task RuleEngine_PostgresIntegration_MultipleMatchingRules_OrderedByPriority()
    {
        // Arrange
        var (_, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(7777, "rule-test/priority-repo");

        var lowPriRuleId = Guid.NewGuid();
        var highPriRuleId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var lowPriRule = new Rule
            {
                Id = lowPriRuleId,
                RepositoryId = repoId,
                Name = "Low Priority Rule",
                EventType = "issues.opened",
                IsEnabled = true,
                Priority = 10,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "bug" }
                },
                Actions = new List<RuleAction>
                {
                    new() { ActionType = ActionType.SlackNotification, ExecutionOrder = 1 }
                }
            };

            var highPriRule = new Rule
            {
                Id = highPriRuleId,
                RepositoryId = repoId,
                Name = "High Priority Rule",
                EventType = "issues.opened",
                IsEnabled = true,
                Priority = 1,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "bug" }
                },
                Actions = new List<RuleAction>
                {
                    new() { ActionType = ActionType.GithubAddLabel, ExecutionOrder = 0 }
                }
            };

            context.Rules.AddRange(lowPriRule, highPriRule);
            await context.SaveChangesAsync();
        }

        // Act: Evaluate with RuleEngine using repository
        await using var queryContext = PostgresTestHelper.CreateDbContext();
        var ruleRepo = new RuleRepository(queryContext);
        var rules = await ruleRepo.GetActiveRulesForEventAsync(repoId, "issues.opened");

        var ruleEngine = new RuleEngine();
        var webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            EventType = "issues",
            Action = "opened",
            RawPayload = "{\"action\":\"opened\",\"issue\":{\"title\":\"Critical bug\",\"user\":{\"login\":\"dev\"},\"labels\":[]}}"
        };

        var matches = ruleEngine.EvaluateRules(rules, webhookEvent);

        // Assert
        matches.Should().HaveCount(2);
        matches[0].RuleId.Should().Be(highPriRuleId, "High priority rule (Priority = 1) must be first");
        matches[1].RuleId.Should().Be(lowPriRuleId, "Low priority rule (Priority = 10) must be second");
    }
}
