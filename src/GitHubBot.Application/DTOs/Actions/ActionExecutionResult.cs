using GitHubBot.Domain.Entities;

namespace GitHubBot.Application.DTOs.Actions;

public class ActionExecutionResult
{
    public bool Success { get; }
    public bool WasSkipped { get; }
    public string? ErrorMessage { get; }
    public bool IsTransient { get; }
    public ActionExecution? Execution { get; }

    private ActionExecutionResult(
        bool success,
        bool wasSkipped,
        string? errorMessage,
        bool isTransient,
        ActionExecution? execution)
    {
        Success = success;
        WasSkipped = wasSkipped;
        ErrorMessage = errorMessage;
        IsTransient = isTransient;
        Execution = execution;
    }

    public static ActionExecutionResult Succeeded(ActionExecution execution)
        => new(true, false, null, false, execution);

    public static ActionExecutionResult Skipped(ActionExecution execution)
        => new(true, true, null, false, execution);

    public static ActionExecutionResult Failed(string errorMessage, bool isTransient, ActionExecution? execution = null)
        => new(false, false, errorMessage, isTransient, execution);
}
