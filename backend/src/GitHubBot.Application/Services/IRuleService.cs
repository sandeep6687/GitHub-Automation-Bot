using GitHubBot.Application.DTOs.Rules;

namespace GitHubBot.Application.Services;

public interface IRuleService
{
    Task<IReadOnlyList<RuleResponseDto>> GetRulesAsync(Guid userId, Guid repositoryId, CancellationToken cancellationToken = default);
    Task<RuleResponseDto> GetRuleByIdAsync(Guid userId, Guid repositoryId, Guid ruleId, CancellationToken cancellationToken = default);
    Task<RuleResponseDto> CreateRuleAsync(Guid userId, Guid repositoryId, CreateRuleRequest request, CancellationToken cancellationToken = default);
    Task<RuleResponseDto> UpdateRuleAsync(Guid userId, Guid repositoryId, Guid ruleId, UpdateRuleRequest request, CancellationToken cancellationToken = default);
    Task<RuleResponseDto> ToggleRuleAsync(Guid userId, Guid repositoryId, Guid ruleId, bool enabled, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(Guid userId, Guid repositoryId, Guid ruleId, CancellationToken cancellationToken = default);
}
