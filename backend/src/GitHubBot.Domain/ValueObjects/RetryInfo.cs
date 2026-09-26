namespace GitHubBot.Domain.ValueObjects;

public sealed record RetryInfo(
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextRetryAt,
    DateTime? ClaimedAt,
    string? LastError,
    DateTime? ProcessedAt);
