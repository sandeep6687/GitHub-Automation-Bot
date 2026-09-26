using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.DTOs.Activity;
using GitHubBot.Application.DTOs.Repository;
using GitHubBot.Application.DTOs.Rules;
using Xunit;

namespace GitHubBot.UnitTests;

public class DashboardDtoSecurityTests
{
    [Fact]
    public void Test16_DTOs_DoNotExposeSecrets()
    {
        var sensitivePropertyNames = new[]
        {
            "Secret", "WebhookSecret", "EncryptedWebhookSecret", "ClientSecret",
            "SlackWebhookUrl", "IncomingWebhookUrl", "Password", "Key"
        };

        var dtoTypes = new[]
        {
            typeof(ConnectedRepoDto),
            typeof(AvailableRepoDto),
            typeof(RuleResponseDto),
            typeof(ConditionResponseDto),
            typeof(ActionResponseDto),
            typeof(ActivityResponseDto),
            typeof(ActivityEventDto),
            typeof(ActivityActionDto)
        };

        foreach (var type in dtoTypes)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                foreach (var sensitiveName in sensitivePropertyNames)
                {
                    prop.Name.Should().NotContain(sensitiveName,
                        because: $"DTO type {type.Name} must not expose sensitive property {prop.Name}");
                }
            }
        }
    }

    [Fact]
    public void Test17_DTOs_DoNotExposeTokens()
    {
        var tokenPropertyNames = new[]
        {
            "Token", "AccessToken", "EncryptedAccessToken", "RefreshToken", "AuthToken"
        };

        var dtoTypes = new[]
        {
            typeof(ConnectedRepoDto),
            typeof(AvailableRepoDto),
            typeof(RuleResponseDto),
            typeof(ConditionResponseDto),
            typeof(ActionResponseDto),
            typeof(ActivityResponseDto),
            typeof(ActivityEventDto),
            typeof(ActivityActionDto)
        };

        foreach (var type in dtoTypes)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                foreach (var tokenName in tokenPropertyNames)
                {
                    prop.Name.Should().NotContain(tokenName,
                        because: $"DTO type {type.Name} must not expose token property {prop.Name}");
                }
            }
        }
    }

    [Fact]
    public void Test18_SlackWebhookUrl_NeverSerializedInRuleActionDto()
    {
        var actionDto = new ActionResponseDto
        {
            Id = Guid.NewGuid(),
            ActionType = "SlackNotify",
            ExecutionOrder = 1,
            Configuration = JsonDocument.Parse("{\"message\":\"Hello Slack\"}").RootElement
        };

        var serialized = JsonSerializer.Serialize(actionDto);

        serialized.Should().NotContain("hooks.slack.com");
        serialized.Should().NotContain("WebhookUrl");
        serialized.Should().Contain("message");
    }
}
