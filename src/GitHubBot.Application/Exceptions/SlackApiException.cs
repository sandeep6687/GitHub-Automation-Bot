using System.Net;

namespace GitHubBot.Application.Exceptions;

public class SlackApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public bool IsTransient { get; }

    public SlackApiException(HttpStatusCode statusCode, string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
    }
}
