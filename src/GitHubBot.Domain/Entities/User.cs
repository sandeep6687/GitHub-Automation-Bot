namespace GitHubBot.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public GithubAccount? GithubAccount { get; set; }
    public ICollection<ConnectedRepository> ConnectedRepositories { get; set; } = new List<ConnectedRepository>();
}
