using FluentAssertions;
using GitHubBot.Application.Helpers;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.ValueObjects;

namespace GitHubBot.UnitTests;

public class SlackTemplateRendererTests
{
    private readonly ConnectedRepository _repository;
    private readonly WebhookEvent _webhookEvent;
    private readonly WebhookPayloadData _payload;

    public SlackTemplateRendererTests()
    {
        _repository = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            Owner = "octocat",
            Name = "Hello-World",
            FullName = "octocat/Hello-World"
        };

        _webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repository.Id,
            EventType = "issues",
            Action = "opened"
        };

        _payload = new WebhookPayloadData
        {
            Title = "Crash on checkout",
            Author = "dev-user",
            Action = "opened",
            IssueOrPrNumber = 42,
            HtmlUrl = "https://github.com/octocat/Hello-World/issues/42"
        };
    }

    [Fact]
    public void Render_ShouldSubstituteTitle()
    {
        // Arrange (Test 4: {{title}} substitution)
        var template = "Issue title: {{title}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Issue title: Crash on checkout");
    }

    [Fact]
    public void Render_ShouldSubstituteAuthor()
    {
        // Arrange (Test 5: {{author}} substitution)
        var template = "Opened by: {{author}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Opened by: dev-user");
    }

    [Fact]
    public void Render_ShouldSubstituteAction()
    {
        // Arrange (Test 6: {{action}} substitution)
        var template = "Action: {{action}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Action: opened");
    }

    [Fact]
    public void Render_ShouldSubstituteRepository()
    {
        // Arrange (Test 7: {{repository}} substitution)
        var template = "Repo: {{repository}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Repo: octocat/Hello-World");
    }

    [Fact]
    public void Render_ShouldSubstituteEvent()
    {
        // Arrange (Test 8: {{event}} substitution)
        var template = "Event type: {{event}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Event type: issues");
    }

    [Fact]
    public void Render_ShouldSubstituteIssueNumber()
    {
        // Arrange (Test 9: {{issueNumber}} substitution)
        var template = "Issue #{{issueNumber}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Issue #42");
    }

    [Fact]
    public void Render_ShouldSubstituteUrl()
    {
        // Arrange (Test 10: {{url}} substitution)
        var template = "Link: {{url}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert
        result.Should().Be("Link: https://github.com/octocat/Hello-World/issues/42");
    }

    [Fact]
    public void Render_WhenUnknownVariablePresent_ShouldLeaveVariableUnchanged()
    {
        // Arrange (Test 11: Unknown template variable behavior)
        var template = "Hello {{unknown_var}} and {{foo}}!";

        // Act
        var result = SlackTemplateRenderer.Render(template, _payload, _repository, _webhookEvent, 42);

        // Assert: Unknown variables must NOT execute code/script and remain unchanged
        result.Should().Be("Hello {{unknown_var}} and {{foo}}!");
    }

    [Fact]
    public void Render_WhenEventFieldsAreMissingOrNull_ShouldHandleSafelyWithoutCrashing()
    {
        // Arrange (Test 12: Missing event field handled safely)
        var emptyPayload = new WebhookPayloadData();
        var template = "Title: '{{title}}', Author: '{{author}}', URL: '{{url}}', #{{issueNumber}}";

        // Act
        var result = SlackTemplateRenderer.Render(template, emptyPayload, null, null, null);

        // Assert: Nulls are safely replaced with empty strings
        result.Should().Be("Title: '', Author: '', URL: '', #");
    }
}
