namespace GitHubBot.Application.DTOs.Activity;

public class ActivityResponseDto
{
    public List<ActivityEventDto> Items { get; set; } = new();
}
