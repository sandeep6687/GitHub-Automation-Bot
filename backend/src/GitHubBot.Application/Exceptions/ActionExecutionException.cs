namespace GitHubBot.Application.Exceptions;

public class ActionExecutionException : Exception
{
    public bool IsTransient { get; }

    public ActionExecutionException(string message, bool isTransient = true, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
