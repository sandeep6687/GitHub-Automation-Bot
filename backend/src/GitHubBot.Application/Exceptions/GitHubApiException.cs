using System.Net;

namespace GitHubBot.Application.Exceptions;

public class GitHubApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public bool IsTransient { get; }
    public string? ResponseBody { get; }

    public GitHubApiException(HttpStatusCode statusCode, string message, string? responseBody = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        // 429 (rate limiting) or 5xx (server errors) are transient.
        // 401 (auth), 403 (forbidden/rate limit without header or permission), 404 (not found), 422 (validation) are permanent.
        IsTransient = (int)statusCode == 429 || (int)statusCode >= 500;
    }
}
