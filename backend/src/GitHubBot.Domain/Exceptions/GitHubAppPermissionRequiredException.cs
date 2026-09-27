namespace GitHubBot.Domain.Exceptions;

public class GitHubAppPermissionRequiredException : Exception
{
    public GitHubAppPermissionRequiredException(string message) : base(message)
    {
    }

    public GitHubAppPermissionRequiredException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
