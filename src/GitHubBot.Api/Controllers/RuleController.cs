using System.Security.Claims;
using GitHubBot.Application.DTOs.Rules;
using GitHubBot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitHubBot.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/repositories/{repositoryId:guid}/rules")]
public class RuleController : ControllerBase
{
    private readonly IRuleService _ruleService;

    public RuleController(IRuleService ruleService)
    {
        _ruleService = ruleService;
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
    public async Task<IActionResult> GetRules(Guid repositoryId, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var rules = await _ruleService.GetRulesAsync(userId, repositoryId, cancellationToken);
            return Ok(rules);
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

    [HttpGet("{ruleId:guid}")]
    public async Task<IActionResult> GetRule(Guid repositoryId, Guid ruleId, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var rule = await _ruleService.GetRuleByIdAsync(userId, repositoryId, ruleId, cancellationToken);
            return Ok(rule);
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

    [HttpPost]
    public async Task<IActionResult> CreateRule(
        Guid repositoryId,
        [FromBody] CreateRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var created = await _ruleService.CreateRuleAsync(userId, repositoryId, request, cancellationToken);
            return CreatedAtAction(nameof(GetRule), new { repositoryId, ruleId = created.Id }, created);
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

    [HttpPut("{ruleId:guid}")]
    public async Task<IActionResult> UpdateRule(
        Guid repositoryId,
        Guid ruleId,
        [FromBody] UpdateRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var updated = await _ruleService.UpdateRuleAsync(userId, repositoryId, ruleId, request, cancellationToken);
            return Ok(updated);
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

    [HttpPatch("{ruleId:guid}/enabled")]
    public async Task<IActionResult> ToggleRule(
        Guid repositoryId,
        Guid ruleId,
        [FromBody] ToggleRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var updated = await _ruleService.ToggleRuleAsync(userId, repositoryId, ruleId, request.Enabled, cancellationToken);
            return Ok(updated);
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

    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> DeleteRule(Guid repositoryId, Guid ruleId, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            await _ruleService.DeleteRuleAsync(userId, repositoryId, ruleId, cancellationToken);
            return NoContent();
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
