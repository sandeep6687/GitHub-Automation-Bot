using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.DTOs.Rules;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GitHubBot.IntegrationTests;

[Collection("PostgreSqlTests")]
public class DashboardIntegrationTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await PostgresTestHelper.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (RuleService RuleService, ActivityService ActivityService) CreateServices()
    {
        var context = PostgresTestHelper.CreateDbContext();
        var repoRepo = new ConnectedRepositoryRepository(context);
        var ruleRepo = new RuleRepository(context);
        var webhookEventRepo = new WebhookEventRepository(context);

        var ruleService = new RuleService(repoRepo, ruleRepo);
        var activityService = new ActivityService(repoRepo, webhookEventRepo);

        return (ruleService, activityService);
    }

    [Fact]
    public async Task Test01_CreateRule_PersistsCorrectly_InPostgres()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "Issue Auto Labeler",
            EventType = "issues.opened",
            Priority = 1,
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "TitleContains", Value = "bug" }
            },
            Actions = new List<ActionDto>
            {
                new()
                {
                    ActionType = "AddLabel",
                    ExecutionOrder = 1,
                    Configuration = JsonDocument.Parse("{\"label\":\"bug\"}").RootElement
                }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, request);
        created.Should().NotBeNull();
        created.Id.Should().NotBeEmpty();

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var dbRule = await verifyContext.Rules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == created.Id);

        dbRule.Should().NotBeNull();
        dbRule!.Name.Should().Be("Issue Auto Labeler");
        dbRule.EventType.Should().Be("issues.opened");
        dbRule.IsEnabled.Should().BeTrue();
        dbRule.Conditions.Should().HaveCount(1);
        dbRule.Actions.Should().HaveCount(1);
    }

    [Fact]
    public async Task Test02_MultipleConditions_PersistInPostgres()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "Strict Issue Filter",
            EventType = "issues.opened",
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "TitleContains", Value = "urgent", CaseSensitive = true },
                new() { ConditionType = "AuthorEquals", Value = "lead-dev" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"priority\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, request);

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var conditions = await verifyContext.RuleConditions
            .Where(c => c.RuleId == created.Id)
            .OrderBy(c => c.Field)
            .ToListAsync();

        conditions.Should().HaveCount(2);
        conditions.Select(c => c.ConditionType).Should().Contain(new[] { ConditionType.TitleContains, ConditionType.AuthorEquals });
    }

    [Fact]
    public async Task Test03_MultipleActions_PersistInExecutionOrder()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "Multi-Action Workflow",
            EventType = "issues.opened",
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "TitleContains", Value = "help" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "SlackNotify", ExecutionOrder = 2, Configuration = JsonDocument.Parse("{\"message\":\"Slack alert\"}").RootElement },
                new() { ActionType = "AddLabel", ExecutionOrder = 1, Configuration = JsonDocument.Parse("{\"label\":\"triage\"}").RootElement },
                new() { ActionType = "AddComment", ExecutionOrder = 3, Configuration = JsonDocument.Parse("{\"body\":\"Thanks for reporting!\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, request);

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var actions = await verifyContext.RuleActions
            .Where(a => a.RuleId == created.Id)
            .OrderBy(a => a.ExecutionOrder)
            .ToListAsync();

        actions.Should().HaveCount(3);
        actions[0].ExecutionOrder.Should().Be(1);
        actions[0].ActionType.Should().Be(ActionType.GithubAddLabel);
        actions[1].ExecutionOrder.Should().Be(2);
        actions[1].ActionType.Should().Be(ActionType.SlackNotify);
        actions[2].ExecutionOrder.Should().Be(3);
        actions[2].ActionType.Should().Be(ActionType.GithubAddComment);
    }

    [Fact]
    public async Task Test04_Rule_RetrievedCorrectly()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "Queryable Rule",
            EventType = "pull_request.opened",
            Priority = 5,
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "AuthorEquals", Value = "dependabot[bot]" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"dependencies\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, request);
        var retrieved = await ruleService.GetRuleByIdAsync(userId, repoId, created.Id);

        retrieved.Should().NotBeNull();
        retrieved.Name.Should().Be("Queryable Rule");
        retrieved.Priority.Should().Be(5);
        retrieved.Conditions.Should().HaveCount(1);
        retrieved.Actions.Should().HaveCount(1);
    }

    [Fact]
    public async Task Test05_RuleUpdate_Persists()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var initialRequest = new CreateRuleRequest
        {
            Name = "Initial Rule",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"initial\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, initialRequest);

        var updateRequest = new UpdateRuleRequest
        {
            Name = "Updated Rule Title",
            EventType = "issues.labeled",
            Priority = 10,
            IsEnabled = false,
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "LabelContains", Value = "enhancement" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddComment", Configuration = JsonDocument.Parse("{\"body\":\"Updated comment\"}").RootElement }
            }
        };

        var updated = await ruleService.UpdateRuleAsync(userId, repoId, created.Id, updateRequest);
        updated.Name.Should().Be("Updated Rule Title");
        updated.Enabled.Should().BeFalse();

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var dbRule = await verifyContext.Rules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .FirstAsync(r => r.Id == created.Id);

        dbRule.Name.Should().Be("Updated Rule Title");
        dbRule.IsEnabled.Should().BeFalse();
        dbRule.Conditions.Should().ContainSingle(c => c.Value == "enhancement");
        dbRule.Actions.Should().ContainSingle(a => a.ActionType == ActionType.GithubAddComment);
    }

    [Fact]
    public async Task Test06_EnableDisable_Persists()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "Toggle Test Rule",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"test\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, request);
        created.Enabled.Should().BeTrue();

        var toggledOff = await ruleService.ToggleRuleAsync(userId, repoId, created.Id, false);
        toggledOff.Enabled.Should().BeFalse();

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var dbRule = await verifyContext.Rules.FindAsync(created.Id);
        dbRule!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Test07_Delete_RemovesRule_AndCascadesChildren()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync();
        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "To Be Deleted",
            EventType = "issues.opened",
            Conditions = new List<ConditionDto>
            {
                new() { ConditionType = "TitleContains", Value = "obsolete" }
            },
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"temp\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(userId, repoId, request);

        await ruleService.DeleteRuleAsync(userId, repoId, created.Id);

        await using var verifyContext = PostgresTestHelper.CreateDbContext();
        var exists = await verifyContext.Rules.AnyAsync(r => r.Id == created.Id);
        exists.Should().BeFalse();

        var conditionsExist = await verifyContext.RuleConditions.AnyAsync(c => c.RuleId == created.Id);
        conditionsExist.Should().BeFalse();

        var actionsExist = await verifyContext.RuleActions.AnyAsync(a => a.RuleId == created.Id);
        actionsExist.Should().BeFalse();
    }

    [Fact]
    public async Task Test08_CrossUserRepositoryAccess_Rejected()
    {
        var (user1, repo1) = await PostgresTestHelper.SeedUserAndRepositoryAsync(1001, "user1/repo1");
        var (user2, _) = await PostgresTestHelper.SeedUserAndRepositoryAsync(1002, "user2/repo2");
        var (ruleService, _) = CreateServices();

        var act = () => ruleService.GetRulesAsync(user2, repo1);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Test09_CrossRepositoryRuleAccess_Rejected()
    {
        var (user1, repo1) = await PostgresTestHelper.SeedUserAndRepositoryAsync(2001, "user1/repoA");
        
        Guid repo2;
        await using (var seedCtx = PostgresTestHelper.CreateDbContext())
        {
            var r2 = new ConnectedRepository
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                GithubRepositoryId = 2002,
                FullName = "user1/repoB",
                Owner = "user1",
                Name = "repoB",
                EncryptedWebhookSecret = "secret"
            };
            seedCtx.ConnectedRepositories.Add(r2);
            await seedCtx.SaveChangesAsync();
            repo2 = r2.Id;
        }

        var (ruleService, _) = CreateServices();

        var request = new CreateRuleRequest
        {
            Name = "Rule On Repo A",
            EventType = "issues.opened",
            Actions = new List<ActionDto>
            {
                new() { ActionType = "AddLabel", Configuration = JsonDocument.Parse("{\"label\":\"test\"}").RootElement }
            }
        };

        var created = await ruleService.CreateRuleAsync(user1, repo1, request);

        // User attempts to access rule from repo1 under the URL path of repo2
        var act = () => ruleService.GetRuleByIdAsync(user1, repo2, created.Id);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Test10_Activity_ReturnsEventsWithActions()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(3001, "activity/repo");
        var (_, activityService) = CreateServices();

        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            var evt = new WebhookEvent
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                DeliveryId = "delivery-act-10",
                EventType = "issues",
                Action = "opened",
                Status = EventStatus.Success,
                RawPayload = "{}",
                CreatedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow,
                AttemptCount = 1
            };
            seedContext.WebhookEvents.Add(evt);

            var actExec = new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = evt.Id,
                ActionType = ActionType.GithubAddLabel,
                Status = ExecutionStatus.Success,
                DurationMs = 125,
                ExecutedAt = DateTime.UtcNow
            };
            seedContext.ActionExecutions.Add(actExec);

            await seedContext.SaveChangesAsync();
        }

        var activity = await activityService.GetActivityAsync(userId, repoId);

        activity.Items.Should().HaveCount(1);
        activity.Items[0].DeliveryId.Should().Be("delivery-act-10");
        activity.Items[0].EventType.Should().Be("issues");
        activity.Items[0].Status.Should().Be("Success");
        activity.Items[0].Actions.Should().HaveCount(1);
        activity.Items[0].Actions[0].ActionType.Should().Be("AddLabel");
        activity.Items[0].Actions[0].Status.Should().Be("Success");
        activity.Items[0].Actions[0].DurationMs.Should().Be(125);
    }

    [Fact]
    public async Task Test11_Activity_FilteringWorks()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(4001, "filter/repo");
        var (_, activityService) = CreateServices();

        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            seedContext.WebhookEvents.AddRange(
                new WebhookEvent
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repoId,
                    DeliveryId = "del-filter-1",
                    EventType = "issues",
                    Status = EventStatus.Success,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new WebhookEvent
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repoId,
                    DeliveryId = "del-filter-2",
                    EventType = "pull_request",
                    Status = EventStatus.Failed,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                }
            );
            await seedContext.SaveChangesAsync();
        }

        var successOnly = await activityService.GetActivityAsync(userId, repoId, status: "Success");
        successOnly.Items.Should().HaveCount(1);
        successOnly.Items[0].DeliveryId.Should().Be("del-filter-1");

        var prOnly = await activityService.GetActivityAsync(userId, repoId, eventType: "pull_request");
        prOnly.Items.Should().HaveCount(1);
        prOnly.Items[0].DeliveryId.Should().Be("del-filter-2");
    }

    [Fact]
    public async Task Test12_Activity_LimitIsEnforced()
    {
        var (userId, repoId) = await PostgresTestHelper.SeedUserAndRepositoryAsync(5001, "limit/repo");
        var (_, activityService) = CreateServices();

        await using (var seedContext = PostgresTestHelper.CreateDbContext())
        {
            for (int i = 0; i < 15; i++)
            {
                seedContext.WebhookEvents.Add(new WebhookEvent
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repoId,
                    DeliveryId = $"del-limit-{i}",
                    EventType = "push",
                    Status = EventStatus.Success,
                    CreatedAt = DateTime.UtcNow.AddSeconds(i)
                });
            }
            await seedContext.SaveChangesAsync();
        }

        var limited = await activityService.GetActivityAsync(userId, repoId, limit: 5);
        limited.Items.Should().HaveCount(5);
    }
}
