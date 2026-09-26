using FluentAssertions;
using GitHubBot.Domain.Logic;

namespace GitHubBot.UnitTests;

public class WebhookPayloadExtractionTests
{
    [Fact]
    public void ExtractIssueOrPrNumber_FromIssuesEvent_ShouldReturnIssueNumber()
    {
        // Arrange (Test 5: Correct issue number extracted)
        var payload = @"
        {
            ""action"": ""opened"",
            ""issue"": {
                ""number"": 1337,
                ""title"": ""Critical production issue"",
                ""user"": { ""login"": ""octocat"" }
            }
        }";

        // Act
        var number = WebhookPayloadParser.ExtractIssueOrPrNumber(payload, "issues");

        // Assert
        number.Should().Be(1337);
    }

    [Fact]
    public void ExtractIssueOrPrNumber_FromPullRequestEvent_ShouldReturnPrNumber()
    {
        // Arrange (Test 6: Correct PR number extracted)
        var payload = @"
        {
            ""action"": ""opened"",
            ""pull_request"": {
                ""number"": 2048,
                ""title"": ""Feature: add dark mode"",
                ""user"": { ""login"": ""developer"" }
            }
        }";

        // Act
        var number = WebhookPayloadParser.ExtractIssueOrPrNumber(payload, "pull_request");

        // Assert
        number.Should().Be(2048);
    }

    [Fact]
    public void ExtractIssueOrPrNumber_WhenPayloadIsPushEvent_ShouldReturnNull()
    {
        // Arrange
        var payload = @"
        {
            ""ref"": ""refs/heads/main"",
            ""commits"": []
        }";

        // Act
        var number = WebhookPayloadParser.ExtractIssueOrPrNumber(payload, "push");

        // Assert
        number.Should().BeNull();
    }

    [Fact]
    public void ExtractIssueOrPrNumber_WhenPayloadIsMalformed_ShouldReturnNullWithoutThrowing()
    {
        // Arrange
        var malformed = "{ invalid json content ";

        // Act
        var number = WebhookPayloadParser.ExtractIssueOrPrNumber(malformed, "issues");

        // Assert
        number.Should().BeNull();
    }
}
