using GitHubBot.Application.DTOs.Actions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GitHubBot.Application.Services;

public class ActionDispatcher : IActionDispatcher
{
    private readonly IEnumerable<IActionHandler> _handlers;
    private readonly IActionExecutionRepository _actionExecutionRepository;
    private readonly ILogger<ActionDispatcher> _logger;

    public ActionDispatcher(
        IEnumerable<IActionHandler> handlers,
        IActionExecutionRepository actionExecutionRepository,
        ILogger<ActionDispatcher> logger)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _actionExecutionRepository = actionExecutionRepository ?? throw new ArgumentNullException(nameof(actionExecutionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ActionExecutionResult> DispatchActionAsync(
        WebhookEvent webhookEvent,
        RuleAction ruleAction,
        ConnectedRepository repository,
        int? issueOrPrNumber,
        CancellationToken cancellationToken = default)
    {
        if (webhookEvent == null) throw new ArgumentNullException(nameof(webhookEvent));
        if (ruleAction == null) throw new ArgumentNullException(nameof(ruleAction));
        if (repository == null) throw new ArgumentNullException(nameof(repository));

        // 1. Action-level idempotency check: check existing execution for (WebhookEventId, RuleActionId)
        var existingExecution = await _actionExecutionRepository.GetByEventAndActionAsync(
            webhookEvent.Id,
            ruleAction.Id,
            cancellationToken);

        if (existingExecution != null && existingExecution.Status == ExecutionStatus.Success)
        {
            _logger.LogInformation(
                "Action already succeeded for WebhookEvent {WebhookEventId}, RuleAction {RuleActionId}. Skipping duplicate execution.",
                webhookEvent.Id,
                ruleAction.Id);

            return ActionExecutionResult.Skipped(existingExecution);
        }

        // 2. Resolve matching action handler
        var handler = _handlers.FirstOrDefault(h => h.CanHandle(ruleAction.ActionType) || h.ActionType == ruleAction.ActionType);
        if (handler == null)
        {
            var errorMessage = $"Unknown or unsupported action type: {ruleAction.ActionType}";
            _logger.LogError(
                "Handler not found for ActionType {ActionType} on RuleAction {RuleActionId}, WebhookEvent {WebhookEventId}",
                ruleAction.ActionType,
                ruleAction.Id,
                webhookEvent.Id);

            var failedExecution = existingExecution ?? new ActionExecution
            {
                Id = Guid.NewGuid(),
                WebhookEventId = webhookEvent.Id,
                RuleActionId = ruleAction.Id,
                ActionType = ruleAction.ActionType
            };

            failedExecution.Status = ExecutionStatus.Failed;
            failedExecution.ErrorMessage = errorMessage;
            failedExecution.AttemptNumber = webhookEvent.AttemptCount;
            failedExecution.ExecutedAt = DateTime.UtcNow;
            failedExecution.DurationMs = 0;

            if (existingExecution == null)
            {
                await _actionExecutionRepository.AddAsync(failedExecution, cancellationToken);
            }
            else
            {
                await _actionExecutionRepository.UpdateAsync(failedExecution, cancellationToken);
            }

            return ActionExecutionResult.Failed(errorMessage, isTransient: false, failedExecution);
        }

        _logger.LogInformation(
            "Executing action {ActionType} for WebhookEvent {WebhookEventId}, Rule {RuleId}, RuleAction {RuleActionId}, Repository {RepositoryId}, Attempt {AttemptNumber}",
            ruleAction.ActionType,
            webhookEvent.Id,
            ruleAction.RuleId,
            ruleAction.Id,
            repository.Id,
            webhookEvent.AttemptCount);

        // 3. Prepare execution context (no tokens exposed in context)
        var context = new ActionContext(
            webhookEvent,
            ruleAction,
            repository,
            issueOrPrNumber,
            webhookEvent.AttemptCount);

        // 4. Execute external operation (no database locks held)
        var result = await handler.ExecuteAsync(context, cancellationToken);

        // 5. Persist action execution record
        var executionToPersist = existingExecution ?? new ActionExecution
        {
            Id = Guid.NewGuid(),
            WebhookEventId = webhookEvent.Id,
            RuleActionId = ruleAction.Id,
            ActionType = ruleAction.ActionType
        };

        executionToPersist.Status = result.Success ? ExecutionStatus.Success : ExecutionStatus.Failed;
        executionToPersist.RequestPayload = result.RequestPayload;
        executionToPersist.ResponsePayload = result.ResponsePayload;
        executionToPersist.ErrorMessage = result.ErrorMessage;
        executionToPersist.AttemptNumber = webhookEvent.AttemptCount;
        executionToPersist.ExecutedAt = DateTime.UtcNow;
        executionToPersist.DurationMs = result.DurationMs;

        if (existingExecution == null)
        {
            await _actionExecutionRepository.AddAsync(executionToPersist, cancellationToken);
        }
        else
        {
            await _actionExecutionRepository.UpdateAsync(executionToPersist, cancellationToken);
        }

        _logger.LogInformation(
            "Action {ActionType} for WebhookEvent {WebhookEventId}, RuleAction {RuleActionId} completed with status {Status} in {DurationMs}ms",
            ruleAction.ActionType,
            webhookEvent.Id,
            ruleAction.Id,
            executionToPersist.Status,
            result.DurationMs);

        return result.Success
            ? ActionExecutionResult.Succeeded(executionToPersist)
            : ActionExecutionResult.Failed(result.ErrorMessage ?? "Action execution failed.", result.IsTransientError, executionToPersist);
    }
}
