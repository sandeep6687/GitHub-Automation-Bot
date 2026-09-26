using System.Text.Json;

namespace GitHubBot.Application.DTOs.Rules;

public class RuleResponseDto
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public List<ConditionResponseDto> Conditions { get; set; } = new();
    public List<ActionResponseDto> Actions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ConditionResponseDto
{
    public Guid Id { get; set; }
    public string ConditionType { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
}

public class ActionResponseDto
{
    public Guid Id { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public int ExecutionOrder { get; set; }
    public JsonElement Configuration { get; set; }
}
