using System.Security.Claims;
using GitHubBot.Application.DTOs.Repository;
using GitHubBot.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitHubBot.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/repositories")]
public class RepositoryController : ControllerBase
{
    private readonly IRepositoryService _repositoryService;

    public RepositoryController(IRepositoryService repositoryService)
    {
        _repositoryService = repositoryService;
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

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableRepositories(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var repos = await _repositoryService.GetAvailableRepositoriesAsync(userId, cancellationToken);
            return Ok(repos);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetConnectedRepositories(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var repos = await _repositoryService.GetConnectedRepositoriesAsync(userId, cancellationToken);
        return Ok(repos);
    }

    [HttpPost("connect")]
    public async Task<IActionResult> ConnectRepository(
        [FromBody] ConnectRepoRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GithubRepositoryId <= 0)
        {
            return BadRequest(new { error = "Valid GithubRepositoryId is required." });
        }

        try
        {
            var userId = GetUserId();
            var connected = await _repositoryService.ConnectRepositoryAsync(
                userId,
                request.GithubRepositoryId,
                cancellationToken);

            return Ok(connected);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("already connected", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DisconnectRepository(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            await _repositoryService.DisconnectRepositoryAsync(userId, id, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/sync-github-app")]
    public async Task<IActionResult> SyncGitHubAppInstallation(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var repo = await _repositoryService.SyncGitHubAppInstallationAsync(userId, id, cancellationToken);
            return Ok(new
            {
                appInstalled = repo.InstallationId != null,
                installationId = repo.InstallationId
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }
}
