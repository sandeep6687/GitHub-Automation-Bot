namespace GitHubBot.Application.DTOs.Rules;

public class ConditionDto
{
    public string ConditionType { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;
}
