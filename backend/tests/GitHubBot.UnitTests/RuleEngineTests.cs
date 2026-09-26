using FluentAssertions;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Logic;
using GitHubBot.Domain.ValueObjects;

namespace GitHubBot.UnitTests;

public class RuleEngineTests
{
    private readonly RuleEngine _engine = new();
    private readonly Guid _repoId = Guid.NewGuid();

    private Rule CreateTestRule(
        string eventType = "issues.opened",
        int priority = 0,
        bool isEnabled = true,
        Guid? repoId = null)
    {
        return new Rule
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId ?? _repoId,
            Name = "Test Rule",
            EventType = eventType,
            Priority = priority,
            IsEnabled = isEnabled,
            Conditions = new List<RuleCondition>(),
            Actions = new List<RuleAction>()
        };
    }

    // =========================================================================
    // 1. EVENT MATCHING
    // =========================================================================

    [Fact]
    public void EventMatching_IssuesOpened_ShouldMatchIssuesOpenedRule()
    {
        var rule = CreateTestRule("issues.opened");
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = "A critical bug occurred", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void EventMatching_IssuesClosed_ShouldNotMatchIssuesOpenedRule()
    {
        var rule = CreateTestRule("issues.opened");
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = "A critical bug resolved", Action = "closed" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "closed", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void EventMatching_PullRequestOpened_ShouldMatchPullRequestOpenedRule()
    {
        var rule = CreateTestRule("pull_request.opened");
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "fix" });

        var payload = new WebhookPayloadData { Title = "fix: payment validation", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "pull_request", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void EventMatching_IssuesEvent_ShouldNotMatchPullRequestRule()
    {
        var rule = CreateTestRule("pull_request.opened");
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = "bug report", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void EventMatching_SlashSeparator_ShouldMatchEquivalently()
    {
        var rule = CreateTestRule("issues/opened");
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = "bug report", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    // =========================================================================
    // 2. CONDITION TESTS: TitleContains
    // =========================================================================

    [Fact]
    public void TitleContains_PositiveMatch_ShouldReturnTrue()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "crash" });

        var payload = new WebhookPayloadData { Title = "Server crash on startup", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void TitleContains_NegativeMatch_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "crash" });

        var payload = new WebhookPayloadData { Title = "Feature request for dark mode", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void TitleContains_CaseInsensitiveMatch_ShouldReturnTrue()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition
        {
            ConditionType = ConditionType.TitleContains,
            Value = "BUG",
            CaseSensitive = false
        });

        var payload = new WebhookPayloadData { Title = "Found a subtle bug in auth", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void TitleContains_CaseSensitiveMismatch_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition
        {
            ConditionType = ConditionType.TitleContains,
            Value = "BUG",
            CaseSensitive = true
        });

        var payload = new WebhookPayloadData { Title = "Found a subtle bug in auth", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void TitleContains_MissingTitle_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = null, Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    // =========================================================================
    // 3. CONDITION TESTS: AuthorEquals
    // =========================================================================

    [Fact]
    public void AuthorEquals_PositiveMatch_ShouldReturnTrue()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.AuthorEquals, Value = "octocat" });

        var payload = new WebhookPayloadData { Author = "octocat", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void AuthorEquals_NegativeMatch_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.AuthorEquals, Value = "octocat" });

        var payload = new WebhookPayloadData { Author = "otheruser", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void AuthorEquals_CaseInsensitiveBehavior_ShouldMatchWhenNotCaseSensitive()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition
        {
            ConditionType = ConditionType.AuthorEquals,
            Value = "OctoCat",
            CaseSensitive = false
        });

        var payload = new WebhookPayloadData { Author = "octocat", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void AuthorEquals_CaseSensitiveMismatch_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition
        {
            ConditionType = ConditionType.AuthorEquals,
            Value = "OctoCat",
            CaseSensitive = true
        });

        var payload = new WebhookPayloadData { Author = "octocat", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void AuthorEquals_MissingAuthor_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.AuthorEquals, Value = "octocat" });

        var payload = new WebhookPayloadData { Author = null, Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    // =========================================================================
    // 4. CONDITION TESTS: LabelContains
    // =========================================================================

    [Fact]
    public void LabelContains_PositiveMatch_ShouldReturnTrue()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.LabelContains, Value = "backend" });

        var payload = new WebhookPayloadData
        {
            Labels = new[] { "bug", "backend", "priority:high" },
            Action = "opened"
        };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void LabelContains_NegativeMatch_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.LabelContains, Value = "frontend" });

        var payload = new WebhookPayloadData
        {
            Labels = new[] { "bug", "backend", "priority:high" },
            Action = "opened"
        };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void LabelContains_MatchesAnyLabelInList_ShouldReturnTrue()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.LabelContains, Value = "urgent" });

        var payload = new WebhookPayloadData
        {
            Labels = new[] { "documentation", "area:ui", "status:urgent-attention" },
            Action = "opened"
        };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void LabelContains_CaseSensitivity_ShouldRespectCaseSetting()
    {
        var caseSensitiveRule = CreateTestRule();
        caseSensitiveRule.Conditions.Add(new RuleCondition
        {
            ConditionType = ConditionType.LabelContains,
            Value = "BUG",
            CaseSensitive = true
        });

        var caseInsensitiveRule = CreateTestRule();
        caseInsensitiveRule.Conditions.Add(new RuleCondition
        {
            ConditionType = ConditionType.LabelContains,
            Value = "BUG",
            CaseSensitive = false
        });

        var payload = new WebhookPayloadData { Labels = new[] { "bug" }, Action = "opened" };

        _engine.EvaluateRule(caseSensitiveRule, payload, "issues", "opened", _repoId).Matched.Should().BeFalse();
        _engine.EvaluateRule(caseInsensitiveRule, payload, "issues", "opened", _repoId).Matched.Should().BeTrue();
    }

    [Fact]
    public void LabelContains_MissingLabels_ShouldReturnFalse()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.LabelContains, Value = "bug" });

        var payload = new WebhookPayloadData { Labels = Array.Empty<string>(), Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    // =========================================================================
    // 5. MULTIPLE CONDITIONS (AND LOGIC)
    // =========================================================================

    [Fact]
    public void MultipleConditions_TwoMatchingConditions_ShouldMatch()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.AuthorEquals, Value = "sandeep" });

        var payload = new WebhookPayloadData
        {
            Title = "Crash bug in API",
            Author = "sandeep",
            Action = "opened"
        };

        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);
        result.Matched.Should().BeTrue();
    }

    [Fact]
    public void MultipleConditions_OneMatchingOneFailing_ShouldNotMatch()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.AuthorEquals, Value = "sandeep" });

        var payload = new WebhookPayloadData
        {
            Title = "Crash bug in API",
            Author = "alice", // Fails author condition
            Action = "opened"
        };

        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);
        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void MultipleConditions_ThreeConditions_AllMustMatch()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.AuthorEquals, Value = "sandeep" });
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.LabelContains, Value = "urgent" });

        // Case 1: All 3 match
        var matchingPayload = new WebhookPayloadData
        {
            Title = "Fatal bug",
            Author = "sandeep",
            Labels = new[] { "urgent", "backend" },
            Action = "opened"
        };
        _engine.EvaluateRule(rule, matchingPayload, "issues", "opened", _repoId).Matched.Should().BeTrue();

        // Case 2: 2 match, label fails
        var failingPayload = new WebhookPayloadData
        {
            Title = "Fatal bug",
            Author = "sandeep",
            Labels = new[] { "low-priority" },
            Action = "opened"
        };
        _engine.EvaluateRule(rule, failingPayload, "issues", "opened", _repoId).Matched.Should().BeFalse();
    }

    // =========================================================================
    // 6. RULE FILTERING & SCOPING
    // =========================================================================

    [Fact]
    public void RuleFiltering_DisabledRule_ShouldBeIgnored()
    {
        var rule = CreateTestRule(isEnabled: false);
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = "bug report", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void RuleFiltering_RuleBelongingToAnotherRepository_ShouldBeIgnored()
    {
        var otherRepoId = Guid.NewGuid();
        var rule = CreateTestRule(repoId: otherRepoId);
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var payload = new WebhookPayloadData { Title = "bug report", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId); // Scoped to _repoId

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void RuleFiltering_ZeroConditionsRule_ShouldNotMatch()
    {
        var rule = CreateTestRule(); // No conditions

        var payload = new WebhookPayloadData { Title = "Any title", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeFalse();
    }

    [Fact]
    public void RuleFiltering_MultipleMatchingRules_ShouldAllBeReturned()
    {
        var rule1 = CreateTestRule(priority: 10);
        rule1.Name = "Rule 1";
        rule1.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var rule2 = CreateTestRule(priority: 5);
        rule2.Name = "Rule 2";
        rule2.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "payment" });

        var rules = new[] { rule1, rule2 };
        var payload = new WebhookPayloadData { Title = "bug in payment flow", Action = "opened" };

        var results = _engine.EvaluateRules(rules, payload, "issues", "opened", _repoId);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void RuleFiltering_RulesOrderedByPriority_Ascending()
    {
        var lowPriorityRule = CreateTestRule(priority: 100);
        lowPriorityRule.Name = "Low Priority";
        lowPriorityRule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var highPriorityRule = CreateTestRule(priority: 1);
        highPriorityRule.Name = "High Priority";
        highPriorityRule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var mediumPriorityRule = CreateTestRule(priority: 50);
        mediumPriorityRule.Name = "Medium Priority";
        mediumPriorityRule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var rules = new[] { lowPriorityRule, highPriorityRule, mediumPriorityRule };
        var payload = new WebhookPayloadData { Title = "crash bug", Action = "opened" };

        var results = _engine.EvaluateRules(rules, payload, "issues", "opened", _repoId);

        results.Should().HaveCount(3);
        results[0].RuleName.Should().Be("High Priority");
        results[0].Priority.Should().Be(1);
        results[1].RuleName.Should().Be("Medium Priority");
        results[1].Priority.Should().Be(50);
        results[2].RuleName.Should().Be("Low Priority");
        results[2].Priority.Should().Be(100);
    }

    [Fact]
    public void ActionOrdering_ActionsOrderedByExecutionOrder_Ascending()
    {
        var rule = CreateTestRule();
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "bug" });

        var action2 = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.SlackNotification,
            ExecutionOrder = 2
        };
        var action1 = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddLabel,
            ExecutionOrder = 1
        };
        var action0 = new RuleAction
        {
            Id = Guid.NewGuid(),
            ActionType = ActionType.GithubAddComment,
            ExecutionOrder = 0
        };

        // Added out of order
        rule.Actions.Add(action2);
        rule.Actions.Add(action1);
        rule.Actions.Add(action0);

        var payload = new WebhookPayloadData { Title = "bug alert", Action = "opened" };
        var result = _engine.EvaluateRule(rule, payload, "issues", "opened", _repoId);

        result.Matched.Should().BeTrue();
        result.Actions.Should().HaveCount(3);
        result.Actions[0].ExecutionOrder.Should().Be(0);
        result.Actions[1].ExecutionOrder.Should().Be(1);
        result.Actions[2].ExecutionOrder.Should().Be(2);
    }

    // =========================================================================
    // 7. JSON PARSER & EDGE CASES
    // =========================================================================

    [Fact]
    public void WebhookPayloadParser_IssuesJson_ShouldExtractAllFieldsCorrectly()
    {
        const string json = @"
        {
            ""action"": ""opened"",
            ""issue"": {
                ""title"": ""Crash on checkout"",
                ""user"": {
                    ""login"": ""sandeep""
                },
                ""labels"": [
                    { ""name"": ""bug"" },
                    { ""name"": ""payments"" }
                ]
            }
        }";

        var payload = WebhookPayloadParser.Parse(json, "issues");

        payload.Action.Should().Be("opened");
        payload.Title.Should().Be("Crash on checkout");
        payload.Author.Should().Be("sandeep");
        payload.Labels.Should().BeEquivalentTo(new[] { "bug", "payments" });
    }

    [Fact]
    public void WebhookPayloadParser_PullRequestJson_ShouldExtractAllFieldsCorrectly()
    {
        const string json = @"
        {
            ""action"": ""opened"",
            ""pull_request"": {
                ""title"": ""feat: support dark mode"",
                ""user"": {
                    ""login"": ""designer123""
                },
                ""labels"": [
                    { ""name"": ""ui"" }
                ]
            }
        }";

        var payload = WebhookPayloadParser.Parse(json, "pull_request");

        payload.Action.Should().Be("opened");
        payload.Title.Should().Be("feat: support dark mode");
        payload.Author.Should().Be("designer123");
        payload.Labels.Should().BeEquivalentTo(new[] { "ui" });
    }

    [Fact]
    public void WebhookPayloadParser_MalformedJson_ShouldNotCrash()
    {
        const string malformed = "{ this is not valid json";

        var payload = WebhookPayloadParser.Parse(malformed, "issues");

        payload.Should().NotBeNull();
        payload.Title.Should().BeNull();
        payload.Author.Should().BeNull();
        payload.Labels.Should().BeEmpty();
    }

    [Fact]
    public void WebhookPayloadParser_EmptyOrNullJson_ShouldNotCrash()
    {
        var payload1 = WebhookPayloadParser.Parse("", "issues");
        var payload2 = WebhookPayloadParser.Parse(null, "issues");

        payload1.Title.Should().BeNull();
        payload2.Title.Should().BeNull();
    }

    [Fact]
    public void EvaluateRules_EmptyRules_ShouldReturnEmptyResults()
    {
        var payload = new WebhookPayloadData { Title = "bug", Action = "opened" };
        var results = _engine.EvaluateRules(Array.Empty<Rule>(), payload, "issues", "opened", _repoId);

        results.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateRules_WithWebhookEventEntity_ShouldParseAndEvaluateCorrectly()
    {
        var rule = CreateTestRule("issues.opened");
        rule.Conditions.Add(new RuleCondition { ConditionType = ConditionType.TitleContains, Value = "critical" });
        rule.Actions.Add(new RuleAction { ActionType = ActionType.GithubAddLabel, ExecutionOrder = 1 });

        var webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repoId,
            EventType = "issues",
            Action = "opened",
            RawPayload = "{\"action\":\"opened\",\"issue\":{\"title\":\"critical error in auth\",\"user\":{\"login\":\"admin\"},\"labels\":[]}}"
        };

        var results = _engine.EvaluateRules(new[] { rule }, webhookEvent);

        results.Should().ContainSingle();
        results[0].Matched.Should().BeTrue();
        results[0].Actions.Should().ContainSingle();
    }
}
