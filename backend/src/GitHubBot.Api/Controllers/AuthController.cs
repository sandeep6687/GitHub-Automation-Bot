using System.Security.Claims;
using GitHubBot.Application.DTOs.Auth;
using GitHubBot.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitHubBot.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    public const string StateCookieName = "oauth_state";

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login()
    {
        var authUrl = _authService.GenerateAuthorizationUrl(out var state);

        var isHttps = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        Response.Cookies.Append(StateCookieName, state, cookieOptions);

        return Redirect(authUrl);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(StateCookieName, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return BadRequest(new { error = "Missing OAuth state cookie." });
        }

        // Delete state cookie immediately to prevent replay/reuse attacks
        var isHttps = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
        Response.Cookies.Delete(StateCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax
        });

        if (string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new { error = "Missing state query parameter." });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { error = "Missing authorization code." });
        }

        try
        {
            var userProfile = await _authService.ProcessOAuthCallbackAsync(
                code,
                state,
                expectedState,
                cancellationToken);

            // Establish authenticated session via HttpOnly cookie
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userProfile.Id.ToString()),
                new(ClaimTypes.Name, userProfile.Login),
                new(ClaimTypes.Email, userProfile.Email)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var sessionExpiry = _configuration.GetValue("Authentication:SessionExpirationMinutes", 1440);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(sessionExpiry)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);

            if (Request.Headers.Accept.Any(a => a?.Contains("text/html") == true))
            {
                var frontendUrl = _configuration.GetValue<string>("FrontendUrl") ?? "http://localhost:5173";
                return Redirect(frontendUrl);
            }

            return Ok(userProfile);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var profile = await _authService.GetUserProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return NotFound();
        }

        return Ok(profile);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out successfully" });
    }
}
