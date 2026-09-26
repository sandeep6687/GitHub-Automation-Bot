namespace GitHubBot.Application.DTOs.Rules;

public class UpdateRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int Priority { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public List<ConditionDto> Conditions { get; set; } = new();
    public List<ActionDto> Actions { get; set; } = new();
}
