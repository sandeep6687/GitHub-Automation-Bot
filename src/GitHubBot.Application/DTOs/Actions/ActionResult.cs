namespace GitHubBot.Application.DTOs.Actions;

public class ActionResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public string? RequestPayload { get; }
    public string? ResponsePayload { get; }
    public int DurationMs { get; }
    public bool IsTransientError { get; }

    private ActionResult(
        bool success,
        string? errorMessage,
        string? requestPayload,
        string? responsePayload,
        int durationMs,
        bool isTransientError)
    {
        Success = success;
        ErrorMessage = errorMessage;
        RequestPayload = requestPayload;
        ResponsePayload = responsePayload;
        DurationMs = durationMs;
        IsTransientError = isTransientError;
    }

    public static ActionResult Succeeded(
        string? requestPayload,
        string? responsePayload,
        int durationMs)
        => new(true, null, requestPayload, responsePayload, durationMs, false);

    public static ActionResult Failed(
        string errorMessage,
        string? requestPayload,
        string? responsePayload,
        int durationMs,
        bool isTransient = true)
        => new(false, errorMessage, requestPayload, responsePayload, durationMs, isTransient);
}
