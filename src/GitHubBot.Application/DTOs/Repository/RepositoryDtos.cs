namespace GitHubBot.Application.DTOs.Repository;

public class AvailableRepoDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public bool IsPrivate { get; set; }
    public bool IsConnected { get; set; }
}

public class ConnectedRepoDto
{
    public Guid Id { get; set; }
    public long GithubRepositoryId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ConnectRepoRequest
{
    public long GithubRepositoryId { get; set; }
}
