using System.Net;
using FluentAssertions;
using GitHubBot.Application.Configuration;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Exceptions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Logic;
using GitHubBot.Infrastructure.ActionHandlers;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GitHubBot.IntegrationTests;

[Collection("PostgreSqlTests")]
public class SlackNotificationIntegrationTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await PostgresTestHelper.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SlackActionExecution_ShouldPersistCorrectlyInPostgres()
    {
        // Arrange (Integration 1: Slack ActionExecution persists correctly)
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "SlackNotificationRule",
                EventType = "issues.opened",
                IsEnabled = true
            };
            context.Rules.Add(rule);

            var action = new RuleAction
            {
                Id = actionId,
                RuleId = rule.Id,
                ActionType = ActionType.SlackNotify,
                Configuration = "{\"message\":\"Bug alert\"}"
            };
            context.RuleActions.Add(action);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-slack-persist",
                EventType = "issues",
                Action = "opened",
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":10,\"title\":\"Bug\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        // Act: Persist Slack ActionExecution
        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var repo = new ActionExecutionRepository(context);
            var execution = new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = eventId,
                RuleActionId = actionId,
                ActionType = ActionType.SlackNotify,
                Status = ExecutionStatus.Success,
                RequestPayload = "{\"text\":\"Bug alert\"}",
                ResponsePayload = "{\"status\":\"ok\"}",
                AttemptNumber = 1,
                ExecutedAt = DateTime.UtcNow,
                DurationMs = 75
            };

            await repo.AddAsync(execution);
        }

        // Assert: Read back from PostgreSQL
        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var repo = new ActionExecutionRepository(verifyContext);
            var retrieved = await repo.GetByEventAndActionAsync(eventId, actionId);

            retrieved.Should().NotBeNull();
            retrieved!.ActionType.Should().Be(ActionType.SlackNotify);
            retrieved.Status.Should().Be(ExecutionStatus.Success);
            retrieved.RequestPayload.Should().Contain("Bug alert");
            retrieved.ResponsePayload.Should().Contain("ok");
            retrieved.DurationMs.Should().Be(75);
        }
    }

    [Fact]
    public async Task SlackActionExecution_UniqueConstraint_ShouldPreventDuplicateRecordForSameEventAndAction()
    {
        // Arrange (Integration 2: Unique (WebhookEventId, RuleActionId) is preserved)
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "SlackRule",
                EventType = "issues.opened"
            };
            context.Rules.Add(rule);

            var action = new RuleAction
            {
                Id = actionId,
                RuleId = rule.Id,
                ActionType = ActionType.SlackNotify
            };
            context.RuleActions.Add(action);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-slack-unique",
                EventType = "issues",
                Action = "opened"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        // Act: Insert first execution
        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var repo = new ActionExecutionRepository(context);
            await repo.AddAsync(new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = eventId,
                RuleActionId = actionId,
                ActionType = ActionType.SlackNotify,
                Status = ExecutionStatus.Success,
                AttemptNumber = 1
            });
        }

        // Assert: Second insertion violates PostgreSQL unique constraint
        await using (var duplicateContext = PostgresTestHelper.CreateDbContext())
        {
            var duplicateRepo = new ActionExecutionRepository(duplicateContext);
            var duplicateAct = () => duplicateRepo.AddAsync(new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = eventId,
                RuleActionId = actionId,
                ActionType = ActionType.SlackNotify,
                Status = ExecutionStatus.Pending,
                AttemptNumber = 2
            });

            await duplicateAct.Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task ProcessEventAsync_WithGitHubActionAndSlackAction_ShouldPersistBothExecutions()
    {
        // Arrange (Integration 3: Multiple actions persist correctly)
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var labelActionId = Guid.NewGuid();
        var slackActionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "LabelAndSlackRule",
                EventType = "issues.opened",
                Priority = 1,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "urgent" }
                }
            };
            context.Rules.Add(rule);

            var action1 = new RuleAction
            {
                Id = labelActionId,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddLabel,
                Configuration = "{\"label\":\"urgent\"}",
                ExecutionOrder = 1
            };
            var action2 = new RuleAction
            {
                Id = slackActionId,
                RuleId = rule.Id,
                ActionType = ActionType.SlackNotify,
                Configuration = "{\"message\":\"Urgent issue: {{title}}\"}",
                ExecutionOrder = 2
            };
            context.RuleActions.AddRange(action1, action2);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-label-slack",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":55,\"title\":\"urgent database latency\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        // Act: Execute via pipeline
        await using (var execContext = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(execContext);
            var ruleRepo = new RuleRepository(execContext);
            var actionExecRepo = new ActionExecutionRepository(execContext);
            var connectedRepoRepo = new ConnectedRepositoryRepository(execContext);

            var labelHandlerMock = new Mock<IActionHandler>();
            labelHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);
            labelHandlerMock.Setup(h => h.CanHandle(ActionType.GithubAddLabel)).Returns(true);
            labelHandlerMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionResult.Succeeded("{\"label\":\"urgent\"}", "{\"labels\":[\"urgent\"]}", 30));

            var slackHandlerMock = new Mock<IActionHandler>();
            slackHandlerMock.Setup(h => h.ActionType).Returns(ActionType.SlackNotify);
            slackHandlerMock.Setup(h => h.CanHandle(ActionType.SlackNotify)).Returns(true);
            slackHandlerMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionResult.Succeeded("{\"text\":\"Urgent issue: urgent database latency\"}", "{\"status\":\"ok\"}", 45));

            var dispatcher = new ActionDispatcher(
                new[] { labelHandlerMock.Object, slackHandlerMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();

            var result = await service.ProcessEventAsync(claimed[0]);
            result.Succeeded.Should().BeTrue();
        }

        // Assert: Both actions persisted with SUCCESS
        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var executions = await verifyContext.ActionExecutions
                .Where(a => a.WebhookEventId == eventId)
                .ToListAsync();

            executions.Should().HaveCount(2);
            executions.Should().OnlyContain(a => a.Status == ExecutionStatus.Success);

            var labelExec = executions.Single(a => a.RuleActionId == labelActionId);
            labelExec.ActionType.Should().Be(ActionType.GithubAddLabel);

            var slackExec = executions.Single(a => a.RuleActionId == slackActionId);
            slackExec.ActionType.Should().Be(ActionType.SlackNotify);
            slackExec.RequestPayload.Should().Contain("Urgent issue: urgent database latency");

            var processedEvent = await verifyContext.WebhookEvents.SingleAsync(e => e.Id == eventId);
            processedEvent.Status.Should().Be(EventStatus.Success);
        }
    }

    [Fact]
    public async Task ProcessEventAsync_WhenGitHubSucceedsAndSlackFails_OnRetryGitHubIsSkippedAndSlackSucceeds()
    {
        // Arrange (Integration 4, 5, 6, 7: GitHub succeeds + Slack fails -> Retry skips GitHub -> Slack retry succeeds -> Event SUCCESS)
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var labelActionId = Guid.NewGuid();
        var slackActionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "PartialSlackRetryRule",
                EventType = "issues.opened",
                Priority = 1,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "incident" }
                }
            };
            context.Rules.Add(rule);

            var action1 = new RuleAction
            {
                Id = labelActionId,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddLabel,
                Configuration = "{\"label\":\"incident\"}",
                ExecutionOrder = 1
            };
            var action2 = new RuleAction
            {
                Id = slackActionId,
                RuleId = rule.Id,
                ActionType = ActionType.SlackNotify,
                Configuration = "{\"message\":\"Incident detected: {{title}}\"}",
                ExecutionOrder = 2
            };
            context.RuleActions.AddRange(action1, action2);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-slack-retry-flow",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":88,\"title\":\"incident on api gateway\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        var labelExecutionCount = 0;
        var slackExecutionCount = 0;

        var labelHandlerMock = new Mock<IActionHandler>();
        labelHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);
        labelHandlerMock.Setup(h => h.CanHandle(ActionType.GithubAddLabel)).Returns(true);
        labelHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => labelExecutionCount++)
            .ReturnsAsync(ActionResult.Succeeded("{\"label\":\"incident\"}", "{\"labels\":[\"incident\"]}", 35));

        var slackHandlerMock = new Mock<IActionHandler>();
        slackHandlerMock.Setup(h => h.ActionType).Returns(ActionType.SlackNotify);
        slackHandlerMock.Setup(h => h.CanHandle(ActionType.SlackNotify)).Returns(true);
        // Attempt 1: Slack fails transiently (e.g. Rate limit 429)
        // Attempt 2: Slack succeeds
        slackHandlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => slackExecutionCount++)
            .ReturnsAsync(() => slackExecutionCount == 1
                ? ActionResult.Failed("Slack API rate limit exceeded (429)", "{}", null, 50, isTransient: true)
                : ActionResult.Succeeded("{\"text\":\"Incident detected\"}", "{\"status\":\"ok\"}", 40));

        // Act: Attempt 1
        await using (var context1 = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(context1);
            var ruleRepo = new RuleRepository(context1);
            var actionExecRepo = new ActionExecutionRepository(context1);
            var connectedRepoRepo = new ConnectedRepositoryRepository(context1);

            var dispatcher = new ActionDispatcher(
                new[] { labelHandlerMock.Object, slackHandlerMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();

            var result = await service.ProcessEventAsync(claimed[0]);
            result.Succeeded.Should().BeFalse();
            result.Retrying.Should().BeTrue(); // Event transitioned to Retrying!
        }

        // Verify state after Attempt 1:
        // GitHub ActionExecution: SUCCESS
        // Slack ActionExecution: FAILED
        // Event: RETRYING
        await using (var verifyContext1 = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext1.WebhookEvents.SingleAsync(e => e.Id == eventId);
            evt.Status.Should().Be(EventStatus.Retrying);

            var labelExec = await verifyContext1.ActionExecutions.SingleAsync(a => a.RuleActionId == labelActionId);
            labelExec.Status.Should().Be(ExecutionStatus.Success);

            var slackExec = await verifyContext1.ActionExecutions.SingleAsync(a => a.RuleActionId == slackActionId);
            slackExec.Status.Should().Be(ExecutionStatus.Failed);

            labelExecutionCount.Should().Be(1);
            slackExecutionCount.Should().Be(1);
        }

        // Fast-forward next_retry_at so event is claimable immediately
        await using (var resetTimeContext = PostgresTestHelper.CreateDbContext())
        {
            await resetTimeContext.Database.ExecuteSqlRawAsync(
                "UPDATE webhook_events SET next_retry_at = NOW() - INTERVAL '1 minute' WHERE id = {0}",
                eventId);
        }

        // Act: Attempt 2 (Retry)
        await using (var context2 = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(context2);
            var ruleRepo = new RuleRepository(context2);
            var actionExecRepo = new ActionExecutionRepository(context2);
            var connectedRepoRepo = new ConnectedRepositoryRepository(context2);

            var dispatcher = new ActionDispatcher(
                new[] { labelHandlerMock.Object, slackHandlerMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();
            claimed[0].AttemptCount.Should().Be(2);

            var result = await service.ProcessEventAsync(claimed[0]);
            result.Succeeded.Should().BeTrue(); // All actions now succeeded!
        }

        // Assert:
        // 1. Label handler was NEVER invoked during Attempt 2! (Skipped due to existing SUCCESS)
        labelExecutionCount.Should().Be(1);

        // 2. Slack handler was invoked a 2nd time and succeeded
        slackExecutionCount.Should().Be(2);

        // 3. PostgreSQL database verification:
        await using (var finalContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await finalContext.WebhookEvents.SingleAsync(e => e.Id == eventId);
            evt.Status.Should().Be(EventStatus.Success);

            var executions = await finalContext.ActionExecutions
                .Where(a => a.WebhookEventId == eventId)
                .ToListAsync();

            // Total executions in PostgreSQL must be exactly 2: NO duplicates!
            executions.Should().HaveCount(2);

            var labelExec = executions.Single(a => a.RuleActionId == labelActionId);
            labelExec.Status.Should().Be(ExecutionStatus.Success);
            labelExec.AttemptNumber.Should().Be(1); // Succeeded on attempt 1

            var slackExec = executions.Single(a => a.RuleActionId == slackActionId);
            slackExec.Status.Should().Be(ExecutionStatus.Success);
            slackExec.AttemptNumber.Should().Be(2); // Succeeded on attempt 2
        }
    }

    [Fact]
    public async Task ProcessEventAsync_WhenPermanentSlackFailureOccurs_EventTransitionsToFailed()
    {
        // Arrange (Integration 8: Permanent Slack failure causes event to become FAILED)
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var slackActionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "PermanentFailureRule",
                EventType = "issues.opened",
                Priority = 1,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "test" }
                }
            };
            context.Rules.Add(rule);

            var action = new RuleAction
            {
                Id = slackActionId,
                RuleId = rule.Id,
                ActionType = ActionType.SlackNotify,
                Configuration = "{\"message\":\"test notification\"}"
            };
            context.RuleActions.Add(action);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-slack-perm-fail",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":1,\"title\":\"test error\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        await using (var execContext = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(execContext);
            var ruleRepo = new RuleRepository(execContext);
            var actionExecRepo = new ActionExecutionRepository(execContext);
            var connectedRepoRepo = new ConnectedRepositoryRepository(execContext);

            var slackHandlerMock = new Mock<IActionHandler>();
            slackHandlerMock.Setup(h => h.ActionType).Returns(ActionType.SlackNotify);
            slackHandlerMock.Setup(h => h.CanHandle(ActionType.SlackNotify)).Returns(true);
            slackHandlerMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionResult.Failed("Slack rejected request (400 Bad Request).", "{}", null, 25, isTransient: false));

            var dispatcher = new ActionDispatcher(
                new[] { slackHandlerMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();

            var result = await service.ProcessEventAsync(claimed[0]);

            // Assert: Permanent failure exhausts retries immediately
            result.Succeeded.Should().BeFalse();
            result.Failed.Should().BeTrue();
            result.Retrying.Should().BeFalse();
        }

        // Verify PostgreSQL status is FAILED
        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext.WebhookEvents.SingleAsync(e => e.Id == eventId);
            evt.Status.Should().Be(EventStatus.Failed);
            evt.LastError.Should().Contain("400 Bad Request");
        }
    }
}
