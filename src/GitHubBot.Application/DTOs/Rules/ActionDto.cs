using System.Text.Json;

namespace GitHubBot.Application.DTOs.Rules;

public class ActionDto
{
    public string ActionType { get; set; } = string.Empty;
    public int ExecutionOrder { get; set; } = 0;
    public JsonElement Configuration { get; set; }
}
