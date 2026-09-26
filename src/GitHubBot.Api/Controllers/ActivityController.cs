using System.Security.Claims;
using GitHubBot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitHubBot.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/repositories/{repositoryId:guid}/activity")]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activityService;

    public ActivityController(IActivityService activityService)
    {
        _activityService = activityService;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid or missing user claim.");
        }
        return userId;
    }

    [HttpGet]
    public async Task<IActionResult> GetActivity(
        Guid repositoryId,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? before = null,
        [FromQuery] string? status = null,
        [FromQuery] string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _activityService.GetActivityAsync(
                userId,
                repositoryId,
                limit,
                before,
                status,
                eventType,
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }
}
