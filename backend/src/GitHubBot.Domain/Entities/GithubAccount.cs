namespace GitHubBot.Domain.Entities;

public class GithubAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long GithubUserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
}
