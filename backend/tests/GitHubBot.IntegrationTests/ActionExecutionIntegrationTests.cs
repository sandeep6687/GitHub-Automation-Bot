using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Logic;
using GitHubBot.Infrastructure.Persistence;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GitHubBot.IntegrationTests;

[Collection("PostgreSqlTests")]
public class ActionExecutionIntegrationTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await PostgresTestHelper.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ActionExecution_Persistence_ShouldSaveAndRetrieveAllFieldsInPostgres()
    {
        // Arrange
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "Auto-triage",
                EventType = "issues.opened"
            };
            context.Rules.Add(rule);

            var ruleAction = new RuleAction
            {
                Id = actionId,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddLabel,
                Configuration = "{\"label\":\"bug\"}"
            };
            context.RuleActions.Add(ruleAction);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-persist-test",
                EventType = "issues",
                Action = "opened",
                RawPayload = "{\"issue\":{\"number\":100}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        // Act: Save ActionExecution via ActionExecutionRepository
        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var repo = new ActionExecutionRepository(context);
            var execution = new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = eventId,
                RuleActionId = actionId,
                ActionType = ActionType.GithubAddLabel,
                Status = ExecutionStatus.Success,
                RequestPayload = "{\"label\":\"bug\"}",
                ResponsePayload = "{\"labels\":[\"bug\"]}",
                AttemptNumber = 1,
                ExecutedAt = DateTime.UtcNow,
                DurationMs = 120
            };

            await repo.AddAsync(execution);
        }

        // Assert: Read back from PostgreSQL
        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var repo = new ActionExecutionRepository(verifyContext);
            var retrieved = await repo.GetByEventAndActionAsync(eventId, actionId);

            retrieved.Should().NotBeNull();
            retrieved!.Status.Should().Be(ExecutionStatus.Success);
            retrieved.ActionType.Should().Be(ActionType.GithubAddLabel);
            retrieved.RequestPayload.Should().Contain("\"label\"").And.Contain("\"bug\"");
            retrieved.ResponsePayload.Should().Contain("\"labels\"").And.Contain("\"bug\"");
            retrieved.AttemptNumber.Should().Be(1);
            retrieved.DurationMs.Should().Be(120);
        }
    }

    [Fact]
    public async Task ActionExecution_UniqueConstraint_ShouldPreventDuplicateExecutionForSameEventAndAction()
    {
        // Arrange
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "Rule",
                EventType = "issues.opened"
            };
            context.Rules.Add(rule);

            var ruleAction = new RuleAction
            {
                Id = actionId,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddLabel
            };
            context.RuleActions.Add(ruleAction);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-unique-test",
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
                ActionType = ActionType.GithubAddLabel,
                Status = ExecutionStatus.Success,
                AttemptNumber = 1
            });
        }

        // Act & Assert: Attempt to insert second execution for same (eventId, actionId) should fail PostgreSQL unique index
        await using (var duplicateContext = PostgresTestHelper.CreateDbContext())
        {
            var duplicateRepo = new ActionExecutionRepository(duplicateContext);
            var duplicateAct = () => duplicateRepo.AddAsync(new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = eventId,
                RuleActionId = actionId,
                ActionType = ActionType.GithubAddLabel,
                Status = ExecutionStatus.Pending,
                AttemptNumber = 2
            });

            await duplicateAct.Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task ProcessEventAsync_WithMultipleActionsForOneEvent_ShouldPersistAllExecutions()
    {
        // Arrange
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var action1Id = Guid.NewGuid();
        var action2Id = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "MultiActionRule",
                EventType = "issues.opened",
                Priority = 1,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "help" }
                }
            };
            context.Rules.Add(rule);

            var action1 = new RuleAction
            {
                Id = action1Id,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddLabel,
                Configuration = "{\"label\":\"triage\"}",
                ExecutionOrder = 1
            };
            var action2 = new RuleAction
            {
                Id = action2Id,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddComment,
                Configuration = "{\"body\":\"Welcome!\"}",
                ExecutionOrder = 2
            };
            context.RuleActions.AddRange(action1, action2);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-multi-actions",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":12,\"title\":\"Need help\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        // Act: Process event end-to-end with real PostgreSQL
        await using (var execContext = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(execContext);
            var ruleRepo = new RuleRepository(execContext);
            var actionExecRepo = new ActionExecutionRepository(execContext);
            var connectedRepoRepo = new ConnectedRepositoryRepository(execContext);

            var labelHandlerMock = new Mock<IActionHandler>();
            labelHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);
            labelHandlerMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionResult.Succeeded("{\"label\":\"triage\"}", "{\"labels\":[\"triage\"]}", 50));

            var commentHandlerMock = new Mock<IActionHandler>();
            commentHandlerMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddComment);
            commentHandlerMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionResult.Succeeded("{\"body\":\"Welcome!\"}", "{\"commentId\":99}", 80));

            var dispatcher = new ActionDispatcher(
                new[] { labelHandlerMock.Object, commentHandlerMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();

            var result = await service.ProcessEventAsync(claimed[0]);
            result.Succeeded.Should().BeTrue();
        }

        // Assert: Verify both action executions were persisted in PostgreSQL
        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var executions = await verifyContext.ActionExecutions
                .Where(a => a.WebhookEventId == eventId)
                .OrderBy(a => a.ActionType)
                .ToListAsync();

            executions.Should().HaveCount(2);
            executions.Should().OnlyContain(a => a.Status == ExecutionStatus.Success);

            var labelExec = executions.Single(a => a.ActionType == ActionType.GithubAddLabel);
            labelExec.RuleActionId.Should().Be(action1Id);
            labelExec.DurationMs.Should().Be(50);

            var commentExec = executions.Single(a => a.ActionType == ActionType.GithubAddComment);
            commentExec.RuleActionId.Should().Be(action2Id);
            commentExec.DurationMs.Should().Be(80);

            var processedEvent = await verifyContext.WebhookEvents.SingleAsync(e => e.Id == eventId);
            processedEvent.Status.Should().Be(EventStatus.Success);
        }
    }

    [Fact]
    public async Task ProcessEventAsync_WithMultipleRules_ShouldExecuteActionsInPriorityOrder()
    {
        // Arrange
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            // Rule 1: Priority 1
            var rule1 = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "RulePriority1",
                EventType = "issues.opened",
                Priority = 1,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "test" }
                }
            };
            var action1 = new RuleAction
            {
                Id = Guid.NewGuid(),
                RuleId = rule1.Id,
                ActionType = ActionType.GithubAddLabel,
                Configuration = "{\"label\":\"priority-1\"}",
                ExecutionOrder = 1
            };
            rule1.Actions.Add(action1);

            // Rule 2: Priority 2
            var rule2 = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "RulePriority2",
                EventType = "issues.opened",
                Priority = 2,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "test" }
                }
            };
            var action2 = new RuleAction
            {
                Id = Guid.NewGuid(),
                RuleId = rule2.Id,
                ActionType = ActionType.GithubAddComment,
                Configuration = "{\"body\":\"priority-2\"}",
                ExecutionOrder = 1
            };
            rule2.Actions.Add(action2);

            context.Rules.AddRange(rule1, rule2);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-multi-rules",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":44,\"title\":\"Multi-rule test\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        // Act
        var executionOrderLog = new List<string>();

        await using (var execContext = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(execContext);
            var ruleRepo = new RuleRepository(execContext);
            var actionExecRepo = new ActionExecutionRepository(execContext);
            var connectedRepoRepo = new ConnectedRepositoryRepository(execContext);

            var labelMock = new Mock<IActionHandler>();
            labelMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);
            labelMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => executionOrderLog.Add("LabelAction"))
                .ReturnsAsync(ActionResult.Succeeded("{}", "{}", 20));

            var commentMock = new Mock<IActionHandler>();
            commentMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddComment);
            commentMock
                .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => executionOrderLog.Add("CommentAction"))
                .ReturnsAsync(ActionResult.Succeeded("{}", "{}", 30));

            var dispatcher = new ActionDispatcher(
                new[] { labelMock.Object, commentMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            var result = await service.ProcessEventAsync(claimed[0]);
            result.Succeeded.Should().BeTrue();
        }

        // Assert: Priority 1 executed before Priority 2
        executionOrderLog.Should().ContainInOrder("LabelAction", "CommentAction");

        await using (var verifyContext = PostgresTestHelper.CreateDbContext())
        {
            var executions = await verifyContext.ActionExecutions
                .Where(a => a.WebhookEventId == eventId)
                .ToListAsync();

            executions.Should().HaveCount(2);
            executions.Should().OnlyContain(a => a.Status == ExecutionStatus.Success);
        }
    }

    [Fact]
    public async Task ProcessEventAsync_WhenAction1SucceedsAndAction2Fails_OnRetryAction1IsSkippedAndAction2Succeeds()
    {
        // Arrange: 1 rule with 2 actions
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var eventId = Guid.NewGuid();
        var action1Id = Guid.NewGuid();
        var action2Id = Guid.NewGuid();

        await using (var context = PostgresTestHelper.CreateDbContext())
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                Name = "PartialFailureRule",
                EventType = "issues.opened",
                Priority = 1,
                IsEnabled = true,
                Conditions = new List<RuleCondition>
                {
                    new() { ConditionType = ConditionType.TitleContains, Value = "Bug" }
                }
            };
            var action1 = new RuleAction
            {
                Id = action1Id,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddLabel,
                Configuration = "{\"label\":\"bug\"}",
                ExecutionOrder = 1
            };
            var action2 = new RuleAction
            {
                Id = action2Id,
                RuleId = rule.Id,
                ActionType = ActionType.GithubAddComment,
                Configuration = "{\"body\":\"Greeting\"}",
                ExecutionOrder = 2
            };
            context.Rules.Add(rule);
            context.RuleActions.AddRange(action1, action2);

            var webhookEvent = new WebhookEvent
            {
                Id = eventId,
                RepositoryId = repoId,
                DeliveryId = "deliv-partial-retry",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Pending,
                RawPayload = "{\"action\":\"opened\",\"issue\":{\"number\":77,\"title\":\"Bug report\"}}"
            };
            context.WebhookEvents.Add(webhookEvent);

            await context.SaveChangesAsync();
        }

        var labelExecutionCount = 0;
        var commentExecutionCount = 0;

        var labelMock = new Mock<IActionHandler>();
        labelMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddLabel);
        labelMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => labelExecutionCount++)
            .ReturnsAsync(ActionResult.Succeeded("{\"label\":\"bug\"}", "{\"labels\":[\"bug\"]}", 25));

        var commentMock = new Mock<IActionHandler>();
        commentMock.Setup(h => h.ActionType).Returns(ActionType.GithubAddComment);
        // First attempt: Action 2 fails with transient error (e.g. 503)
        // Second attempt: Action 2 succeeds
        commentMock
            .Setup(h => h.ExecuteAsync(It.IsAny<ActionContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => commentExecutionCount++)
            .ReturnsAsync(() => commentExecutionCount == 1
                ? ActionResult.Failed("GitHub 503 Service Unavailable", "{}", null, 40, isTransient: true)
                : ActionResult.Succeeded("{}", "{\"commentId\":123}", 30));

        // Act: Attempt 1
        await using (var context1 = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(context1);
            var ruleRepo = new RuleRepository(context1);
            var actionExecRepo = new ActionExecutionRepository(context1);
            var connectedRepoRepo = new ConnectedRepositoryRepository(context1);

            var dispatcher = new ActionDispatcher(
                new[] { labelMock.Object, commentMock.Object },
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
        // Action 1: SUCCESS in Postgres
        // Action 2: FAILED in Postgres
        // Event: RETRYING
        await using (var verifyContext1 = PostgresTestHelper.CreateDbContext())
        {
            var evt = await verifyContext1.WebhookEvents.SingleAsync(e => e.Id == eventId);
            evt.Status.Should().Be(EventStatus.Retrying);

            var action1Exec = await verifyContext1.ActionExecutions.SingleAsync(a => a.RuleActionId == action1Id);
            action1Exec.Status.Should().Be(ExecutionStatus.Success);

            var action2Exec = await verifyContext1.ActionExecutions.SingleAsync(a => a.RuleActionId == action2Id);
            action2Exec.Status.Should().Be(ExecutionStatus.Failed);

            labelExecutionCount.Should().Be(1);
            commentExecutionCount.Should().Be(1);
        }

        // Act: Attempt 2 (Simulate retry execution)
        // Set next_retry_at to past so it can be claimed
        await using (var resetTimeContext = PostgresTestHelper.CreateDbContext())
        {
            await resetTimeContext.Database.ExecuteSqlRawAsync(
                "UPDATE webhook_events SET next_retry_at = NOW() - INTERVAL '1 minute' WHERE id = {0}",
                eventId);
        }

        await using (var context2 = PostgresTestHelper.CreateDbContext())
        {
            var eventRepo = new WebhookEventRepository(context2);
            var ruleRepo = new RuleRepository(context2);
            var actionExecRepo = new ActionExecutionRepository(context2);
            var connectedRepoRepo = new ConnectedRepositoryRepository(context2);

            var dispatcher = new ActionDispatcher(
                new[] { labelMock.Object, commentMock.Object },
                actionExecRepo,
                NullLogger<ActionDispatcher>.Instance);

            var processor = new RuleExecutionProcessor(ruleRepo, new RuleEngine(), dispatcher, connectedRepoRepo);
            var service = new EventProcessingService(eventRepo, processor);

            var claimed = await eventRepo.ClaimBatchAsync(1);
            claimed.Should().ContainSingle();
            claimed[0].AttemptCount.Should().Be(2);

            var result = await service.ProcessEventAsync(claimed[0]);
            result.Succeeded.Should().BeTrue(); // Now all actions succeeded!
        }

        // Assert:
        // 1. Label handler was NEVER invoked during attempt 2! (Skipped due to existing SUCCESS)
        labelExecutionCount.Should().Be(1);

        // 2. Comment handler was invoked a 2nd time and succeeded
        commentExecutionCount.Should().Be(2);

        // 3. PostgreSQL database verification:
        await using (var finalContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = await finalContext.WebhookEvents.SingleAsync(e => e.Id == eventId);
            evt.Status.Should().Be(EventStatus.Success);

            var executions = await finalContext.ActionExecutions
                .Where(a => a.WebhookEventId == eventId)
                .ToListAsync();

            // Total executions must be exactly 2: NO duplicate records created!
            executions.Should().HaveCount(2);

            var action1Exec = executions.Single(a => a.RuleActionId == action1Id);
            action1Exec.Status.Should().Be(ExecutionStatus.Success);
            action1Exec.AttemptNumber.Should().Be(1); // Still attempt 1

            var action2Exec = executions.Single(a => a.RuleActionId == action2Id);
            action2Exec.Status.Should().Be(ExecutionStatus.Success);
            action2Exec.AttemptNumber.Should().Be(2); // Updated to attempt 2
        }
    }
}
