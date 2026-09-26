namespace GitHubBot.Domain.Enums;

public enum ActionType
{
    GithubAddLabel,
    GithubAddComment,
    SlackNotification,
    SlackNotify,
    AddLabel = GithubAddLabel,
    AddComment = GithubAddComment
}
