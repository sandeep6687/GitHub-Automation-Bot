namespace GitHubBot.Infrastructure.BackgroundWorkers;

public class WorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 10;
    public int PollIntervalSeconds { get; set; } = 2;
    public int LockTimeoutMinutes { get; set; } = 5;
    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
    public TimeSpan StaleThreshold => TimeSpan.FromMinutes(LockTimeoutMinutes);
    public TimeSpan StaleCheckInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan ErrorBackoff { get; set; } = TimeSpan.FromSeconds(5);
}
