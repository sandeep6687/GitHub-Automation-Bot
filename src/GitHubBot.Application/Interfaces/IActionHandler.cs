using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Domain.Enums;

namespace GitHubBot.Application.Interfaces;

public interface IActionHandler
{
    ActionType ActionType { get; }
    bool CanHandle(ActionType actionType) => actionType == ActionType;
    Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default);
}
