using GitHubBot.Domain.Logic;
using FluentAssertions;

namespace GitHubBot.UnitTests;

public class RetryCalculatorTests
{
    [Fact]
    public void CanRetry_ShouldReturnTrue_WhenAttemptCountBelowMax()
    {
        RetryCalculator.CanRetry(0, 6).Should().BeTrue();
        RetryCalculator.CanRetry(1, 6).Should().BeTrue();
        RetryCalculator.CanRetry(5, 6).Should().BeTrue();
    }

    [Fact]
    public void CanRetry_ShouldReturnFalse_WhenAttemptCountReachesOrExceedsMax()
    {
        RetryCalculator.CanRetry(6, 6).Should().BeFalse();
        RetryCalculator.CanRetry(7, 6).Should().BeFalse();
    }

    [Fact]
    public void CalculateNextRetry_ShouldReturnNull_WhenMaxAttemptsExhausted()
    {
        var next = RetryCalculator.CalculateNextRetry(6, 6);
        next.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 30)]    // base delay 30s
    [InlineData(2, 120)]   // base delay 2m
    [InlineData(3, 600)]   // base delay 10m
    [InlineData(4, 1800)]  // base delay 30m
    [InlineData(5, 3600)]  // base delay 60m
    public void CalculateNextRetry_ShouldReturnJitteredTimestampWithinBounds(int attempt, int expectedBaseSeconds)
    {
        var before = DateTime.UtcNow;
        var next = RetryCalculator.CalculateNextRetry(attempt, 6);

        next.Should().NotBeNull();
        var delay = next!.Value - before;

        // ±20% jitter tolerance (+ 2s for execution time)
        var minAllowed = TimeSpan.FromSeconds(expectedBaseSeconds * 0.78);
        var maxAllowed = TimeSpan.FromSeconds(expectedBaseSeconds * 1.25);

        delay.Should().BeGreaterThanOrEqualTo(minAllowed);
        delay.Should().BeLessThanOrEqualTo(maxAllowed);
    }
}
