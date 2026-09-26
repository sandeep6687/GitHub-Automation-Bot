namespace GitHubBot.Domain.Logic;

// Pure domain logic. No I/O, no dependencies.
public static class RetryCalculator
{
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60)
    };

    public static bool CanRetry(int attemptCount, int maxAttempts)
        => attemptCount < maxAttempts;

    public static DateTime? CalculateNextRetry(int attemptCount, int maxAttempts)
    {
        if (!CanRetry(attemptCount, maxAttempts))
            return null;

        var index = Math.Min(attemptCount - 1, Delays.Length - 1);
        var baseDelay = Delays[Math.Max(0, index)];

        // Jitter: ±20%
        var jitterMs = Random.Shared.Next(
            (int)(-baseDelay.TotalMilliseconds * 0.2),
            (int)(baseDelay.TotalMilliseconds * 0.2));

        return DateTime.UtcNow + baseDelay + TimeSpan.FromMilliseconds(jitterMs);
    }
}
