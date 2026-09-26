namespace GitHubBot.Domain.Entities;

public class ConnectedRepository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long GithubRepositoryId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public long? WebhookId { get; set; }
    public string EncryptedWebhookSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
    public ICollection<WebhookEvent> WebhookEvents { get; set; } = new List<WebhookEvent>();
}
