using System.Text.Json;
using GitHubBot.Application.DTOs.Rules;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;

namespace GitHubBot.Application.Services;

public class RuleService : IRuleService
{
    private readonly IConnectedRepositoryRepository _connectedRepositoryRepository;
    private readonly IRuleRepository _ruleRepository;

    private static readonly HashSet<string> SupportedEventPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "issues",
        "pull_request",
        "push"
    };

    public RuleService(
        IConnectedRepositoryRepository connectedRepositoryRepository,
        IRuleRepository ruleRepository)
    {
        _connectedRepositoryRepository = connectedRepositoryRepository;
        _ruleRepository = ruleRepository;
    }

    public async Task<IReadOnlyList<RuleResponseDto>> GetRulesAsync(
        Guid userId,
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        await VerifyRepositoryOwnershipAsync(userId, repositoryId, cancellationToken);

        var rules = await _ruleRepository.GetByRepositoryIdAsync(repositoryId, cancellationToken);
        return rules.Select(MapToDto).ToList();
    }

    public async Task<RuleResponseDto> GetRuleByIdAsync(
        Guid userId,
        Guid repositoryId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        await VerifyRepositoryOwnershipAsync(userId, repositoryId, cancellationToken);

        var rule = await _ruleRepository.GetByIdAsync(ruleId, cancellationToken);
        if (rule == null || rule.RepositoryId != repositoryId)
        {
            throw new KeyNotFoundException($"Rule with ID '{ruleId}' not found for this repository.");
        }

        return MapToDto(rule);
    }

    public async Task<RuleResponseDto> CreateRuleAsync(
        Guid userId,
        Guid repositoryId,
        CreateRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        await VerifyRepositoryOwnershipAsync(userId, repositoryId, cancellationToken);
        ValidateRuleData(request.Name, request.EventType, request.Priority, request.Conditions, request.Actions);

        var rule = new Rule
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            Name = request.Name.Trim(),
            EventType = request.EventType.Trim(),
            IsEnabled = true,
            Priority = request.Priority,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (request.Conditions != null)
        {
            foreach (var cond in request.Conditions)
            {
                var condType = Enum.Parse<ConditionType>(cond.ConditionType, true);
                var field = !string.IsNullOrWhiteSpace(cond.Field) ? cond.Field.Trim() : DefaultFieldFor(condType);

                rule.Conditions.Add(new RuleCondition
                {
                    Id = Guid.NewGuid(),
                    RuleId = rule.Id,
                    ConditionType = condType,
                    Field = field,
                    Value = cond.Value.Trim(),
                    CaseSensitive = cond.CaseSensitive
                });
            }
        }

        foreach (var act in request.Actions)
        {
            var actionType = Enum.Parse<ActionType>(act.ActionType, true);
            var configJson = NormalizeAndValidateActionConfig(actionType, act.Configuration);

            rule.Actions.Add(new RuleAction
            {
                Id = Guid.NewGuid(),
                RuleId = rule.Id,
                ActionType = actionType,
                Configuration = configJson,
                ExecutionOrder = act.ExecutionOrder
            });
        }

        var saved = await _ruleRepository.AddAsync(rule, cancellationToken);
        return MapToDto(saved);
    }

    public async Task<RuleResponseDto> UpdateRuleAsync(
        Guid userId,
        Guid repositoryId,
        Guid ruleId,
        UpdateRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        await VerifyRepositoryOwnershipAsync(userId, repositoryId, cancellationToken);
        ValidateRuleData(request.Name, request.EventType, request.Priority, request.Conditions, request.Actions);

        var existingRule = await _ruleRepository.GetByIdAsync(ruleId, cancellationToken);
        if (existingRule == null || existingRule.RepositoryId != repositoryId)
        {
            throw new KeyNotFoundException($"Rule with ID '{ruleId}' not found for this repository.");
        }

        existingRule.Name = request.Name.Trim();
        existingRule.EventType = request.EventType.Trim();
        existingRule.Priority = request.Priority;
        existingRule.IsEnabled = request.IsEnabled;
        existingRule.UpdatedAt = DateTime.UtcNow;

        existingRule.Conditions.Clear();
        if (request.Conditions != null)
        {
            foreach (var cond in request.Conditions)
            {
                var condType = Enum.Parse<ConditionType>(cond.ConditionType, true);
                var field = !string.IsNullOrWhiteSpace(cond.Field) ? cond.Field.Trim() : DefaultFieldFor(condType);

                existingRule.Conditions.Add(new RuleCondition
                {
                    Id = Guid.NewGuid(),
                    RuleId = existingRule.Id,
                    ConditionType = condType,
                    Field = field,
                    Value = cond.Value.Trim(),
                    CaseSensitive = cond.CaseSensitive
                });
            }
        }

        existingRule.Actions.Clear();
        foreach (var act in request.Actions)
        {
            var actionType = Enum.Parse<ActionType>(act.ActionType, true);
            var configJson = NormalizeAndValidateActionConfig(actionType, act.Configuration);

            existingRule.Actions.Add(new RuleAction
            {
                Id = Guid.NewGuid(),
                RuleId = existingRule.Id,
                ActionType = actionType,
                Configuration = configJson,
                ExecutionOrder = act.ExecutionOrder
            });
        }

        await _ruleRepository.UpdateAsync(existingRule, cancellationToken);
        return MapToDto(existingRule);
    }

    public async Task<RuleResponseDto> ToggleRuleAsync(
        Guid userId,
        Guid repositoryId,
        Guid ruleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await VerifyRepositoryOwnershipAsync(userId, repositoryId, cancellationToken);

        var existingRule = await _ruleRepository.GetByIdAsync(ruleId, cancellationToken);
        if (existingRule == null || existingRule.RepositoryId != repositoryId)
        {
            throw new KeyNotFoundException($"Rule with ID '{ruleId}' not found for this repository.");
        }

        existingRule.IsEnabled = enabled;
        existingRule.UpdatedAt = DateTime.UtcNow;

        await _ruleRepository.UpdateAsync(existingRule, cancellationToken);
        return MapToDto(existingRule);
    }

    public async Task DeleteRuleAsync(
        Guid userId,
        Guid repositoryId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        await VerifyRepositoryOwnershipAsync(userId, repositoryId, cancellationToken);

        var existingRule = await _ruleRepository.GetByIdAsync(ruleId, cancellationToken);
        if (existingRule == null || existingRule.RepositoryId != repositoryId)
        {
            throw new KeyNotFoundException($"Rule with ID '{ruleId}' not found for this repository.");
        }

        await _ruleRepository.DeleteAsync(ruleId, cancellationToken);
    }

    private async Task VerifyRepositoryOwnershipAsync(Guid userId, Guid repositoryId, CancellationToken cancellationToken)
    {
        var repo = await _connectedRepositoryRepository.GetByIdAsync(repositoryId, cancellationToken);
        if (repo == null)
        {
            throw new KeyNotFoundException($"Repository with ID '{repositoryId}' was not found.");
        }

        if (repo.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot access rules for a repository you do not own.");
        }
    }

    private static void ValidateRuleData(
        string name,
        string eventType,
        int priority,
        List<ConditionDto>? conditions,
        List<ActionDto>? actions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            throw new ArgumentException("Rule name is required and must not exceed 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("EventType is required.");
        }

        var normalizedEvent = eventType.Trim();
        var baseEvent = normalizedEvent.Contains('.') ? normalizedEvent.Split('.')[0] : normalizedEvent;
        if (!SupportedEventPrefixes.Contains(baseEvent))
        {
            throw new ArgumentException($"Unsupported event type: '{eventType}'. Supported base events are: issues, pull_request, push.");
        }

        if (priority < 0 || priority > 1000)
        {
            throw new ArgumentException("Priority must be between 0 and 1000.");
        }

        if (conditions != null)
        {
            foreach (var cond in conditions)
            {
                if (string.IsNullOrWhiteSpace(cond.ConditionType) ||
                    !Enum.TryParse<ConditionType>(cond.ConditionType, true, out var ct) ||
                    !Enum.IsDefined(ct))
                {
                    throw new ArgumentException($"Invalid condition type: '{cond.ConditionType}'. Supported types: TitleContains, AuthorEquals, LabelContains.");
                }

                if (string.IsNullOrWhiteSpace(cond.Value))
                {
                    throw new ArgumentException("Condition value cannot be empty.");
                }
            }
        }

        if (actions == null || actions.Count == 0)
        {
            throw new ArgumentException("At least one action is required.");
        }

        foreach (var act in actions)
        {
            if (string.IsNullOrWhiteSpace(act.ActionType) ||
                !Enum.TryParse<ActionType>(act.ActionType, true, out var at) ||
                (at != ActionType.AddLabel && at != ActionType.AddComment && at != ActionType.SlackNotify && at != ActionType.SlackNotification && at != ActionType.AiTriage))
            {
                throw new ArgumentException($"Invalid action type: '{act.ActionType}'. Supported actions: AddLabel, AddComment, SlackNotify, AiTriage.");
            }

            NormalizeAndValidateActionConfig(at, act.Configuration);
        }
    }

    private static string NormalizeAndValidateActionConfig(ActionType actionType, JsonElement configElement)
    {
        if (configElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Action configuration for '{actionType}' must be a JSON object.");
        }

        switch (actionType)
        {
            case ActionType.AddLabel:
                if (!configElement.TryGetProperty("label", out var labelProp) ||
                    labelProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(labelProp.GetString()))
                {
                    throw new ArgumentException("AddLabel action requires a non-empty 'label' configuration property.");
                }
                return JsonSerializer.Serialize(new { label = labelProp.GetString()!.Trim() });

            case ActionType.AddComment:
                if (!configElement.TryGetProperty("body", out var bodyProp) ||
                    bodyProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(bodyProp.GetString()))
                {
                    throw new ArgumentException("AddComment action requires a non-empty 'body' configuration property.");
                }
                return JsonSerializer.Serialize(new { body = bodyProp.GetString()!.Trim() });

            case ActionType.SlackNotify:
            case ActionType.SlackNotification:
                string? message = null;
                if (configElement.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                {
                    message = msgProp.GetString();
                }
                else if (configElement.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    message = textProp.GetString();
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    throw new ArgumentException("SlackNotify action requires a non-empty 'message' configuration property.");
                }
                return JsonSerializer.Serialize(new { message = message.Trim() });

            case ActionType.AiTriage:
                return "{}";

            default:
                throw new ArgumentException($"Unsupported action type: {actionType}");
        }
    }

    private static string DefaultFieldFor(ConditionType conditionType) => conditionType switch
    {
        ConditionType.TitleContains => "title",
        ConditionType.AuthorEquals => "author",
        ConditionType.LabelContains => "label",
        _ => "unknown"
    };

    private static RuleResponseDto MapToDto(Rule rule)
    {
        return new RuleResponseDto
        {
            Id = rule.Id,
            RepositoryId = rule.RepositoryId,
            Name = rule.Name,
            EventType = rule.EventType,
            Enabled = rule.IsEnabled,
            Priority = rule.Priority,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
            Conditions = rule.Conditions.Select(c => new ConditionResponseDto
            {
                Id = c.Id,
                ConditionType = c.ConditionType.ToString(),
                Field = c.Field,
                Value = c.Value,
                CaseSensitive = c.CaseSensitive
            }).ToList(),
            Actions = rule.Actions.OrderBy(a => a.ExecutionOrder).Select(a =>
            {
                JsonElement parsedConfig;
                try
                {
                    parsedConfig = JsonDocument.Parse(string.IsNullOrWhiteSpace(a.Configuration) ? "{}" : a.Configuration).RootElement.Clone();
                }
                catch
                {
                    parsedConfig = JsonDocument.Parse("{}").RootElement.Clone();
                }

                return new ActionResponseDto
                {
                    Id = a.Id,
                    ActionType = a.ActionType switch
                    {
                        ActionType.GithubAddLabel => "AddLabel",
                        ActionType.GithubAddComment => "AddComment",
                        ActionType.SlackNotification => "SlackNotify",
                        _ => a.ActionType.ToString()
                    },
                    ExecutionOrder = a.ExecutionOrder,
                    Configuration = parsedConfig
                };
            }).ToList()
        };
    }
}
