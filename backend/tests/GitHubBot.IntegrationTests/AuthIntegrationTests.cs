using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GitHubBot.Api.Controllers;
using GitHubBot.Application.DTOs.Auth;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GitHubBot.IntegrationTests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "TestScheme";
    public static Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUserId.ToString()),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Email, "test@example.com")
        };
        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCurrentUser_Unauthenticated_ShouldReturn401Unauthorized()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrentUser_Authenticated_ShouldReturnUserInfo()
    {
        var mockUserRepository = new Mock<IUserRepository>();
        var testUser = new User
        {
            Id = TestAuthHandler.TestUserId,
            Email = "authenticated@example.com",
            GithubAccount = new GithubAccount
            {
                Id = Guid.NewGuid(),
                UserId = TestAuthHandler.TestUserId,
                GithubUserId = 1234567,
                Login = "octocat_auth",
                AvatarUrl = "https://example.com/avatar.png",
                EncryptedAccessToken = "encrypted_secret_token"
            }
        };

        mockUserRepository
            .Setup(r => r.GetByIdAsync(TestAuthHandler.TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testUser);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockUserRepository.Object);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();

        profile.Should().NotBeNull();
        profile!.Id.Should().Be(TestAuthHandler.TestUserId);
        profile.Email.Should().Be("authenticated@example.com");
        profile.Login.Should().Be("octocat_auth");
        profile.GithubUserId.Should().Be(1234567);

        // Security check: raw or encrypted access token is never in the returned payload
        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("encrypted_secret_token");
        rawJson.Should().NotContain("AccessToken");
    }

    [Fact]
    public async Task Callback_ShouldReject_WhenStateIsInvalidOrMissing()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Missing cookie and missing state
        var response = await client.GetAsync("/api/auth/callback?code=some_code&state=some_state");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: body);
        body.Should().Contain("Missing OAuth state cookie");
    }

    [Fact]
    public async Task Callback_ShouldReject_WhenStateDoesNotMatchCookie()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/callback?code=some_code&state=tampered_state");
        request.Headers.Add("Cookie", $"{AuthController.StateCookieName}=legitimate_state");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("CSRF");
    }

    [Fact]
    public async Task Login_ShouldSetHttpOnlyStateCookie_AndRedirectToGitHub()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/auth/login");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("https://github.com/login/oauth/authorize");

        // Verify Set-Cookie header contains HttpOnly and SameSite=Lax
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var stateCookie = setCookies.FirstOrDefault(c => c.Contains(AuthController.StateCookieName));

        stateCookie.Should().NotBeNull();
        stateCookie!.ToLowerInvariant().Should().Contain("httponly");
        stateCookie.ToLowerInvariant().Should().Contain("samesite=lax");
    }

    [Fact]
    public async Task Callback_ValidFlow_ShouldSetHttpOnlySessionCookie_AndClearStateCookie()
    {
        const string rawToken = "gho_super_secret_github_token";
        const string validCode = "valid_oauth_code";
        const string validState = "matching_secure_state_value";

        var mockOAuthClient = new Mock<IGitHubOAuthClient>();
        mockOAuthClient
            .Setup(c => c.ExchangeCodeForTokenAsync(validCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawToken);

        mockOAuthClient
            .Setup(c => c.GetUserProfileAsync(rawToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubUserResponse
            {
                Id = 778899,
                Login = "secure_dev",
                Email = "secure@example.com",
                AvatarUrl = "https://example.com/avatar.png"
            });

        var mockUserRepository = new Mock<IUserRepository>();
        mockUserRepository
            .Setup(r => r.FindByGithubUserIdAsync(778899, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        mockUserRepository
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockOAuthClient.Object);
                services.AddScoped(_ => mockUserRepository.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/callback?code={validCode}&state={validState}");
        request.Headers.Add("Cookie", $"{AuthController.StateCookieName}={validState}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseBody = await response.Content.ReadAsStringAsync();

        // 1. Access token never appears in API response
        responseBody.Should().NotContain(rawToken);
        responseBody.Should().Contain("secure_dev");
        responseBody.Should().Contain("secure@example.com");

        // 2. Auth session cookie is set
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var sessionCookie = setCookies.FirstOrDefault(c => c.Contains("gh_bot_session"));
        sessionCookie.Should().NotBeNull();

        // 3. Auth session cookie is HttpOnly
        sessionCookie!.ToLowerInvariant().Should().Contain("httponly");

        // 4. Auth session cookie does NOT contain the raw GitHub access token
        sessionCookie.Should().NotContain(rawToken);

        // 5. State cookie is invalidated / cleared to prevent reuse
        var clearedStateCookie = setCookies.FirstOrDefault(c => c.Contains(AuthController.StateCookieName));
        clearedStateCookie.Should().NotBeNull();
        (clearedStateCookie!.Contains("expires=Thu, 01 Jan 1970") || clearedStateCookie.Contains("max-age=0") || clearedStateCookie.Contains("oauth_state=;")).Should().BeTrue();
    }

    [Fact]
    public async Task Logout_Unauthenticated_ShouldReturn401Unauthorized()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_Authenticated_ShouldClearSessionAndReturnSuccess()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Logged out successfully");
    }

    [Fact]
    public async Task ProductionCookie_ShouldHaveSameSiteNone_AndSecure_AndHttpOnly()
    {
        const string rawToken = "gho_prod_test_token";
        const string validCode = "prod_oauth_code";
        const string validState = "prod_state_value";

        var mockOAuthClient = new Mock<IGitHubOAuthClient>();
        mockOAuthClient
            .Setup(c => c.ExchangeCodeForTokenAsync(validCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawToken);

        mockOAuthClient
            .Setup(c => c.GetUserProfileAsync(rawToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubUserResponse
            {
                Id = 998877,
                Login = "prod_user",
                Email = "prod@example.com",
                AvatarUrl = "https://example.com/avatar.png"
            });

        var mockUserRepository = new Mock<IUserRepository>();
        mockUserRepository
            .Setup(r => r.FindByGithubUserIdAsync(998877, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        mockUserRepository
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("FrontendUrl", "https://git-hub-automation-bot.vercel.app");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockOAuthClient.Object);
                services.AddScoped(_ => mockUserRepository.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/callback?code={validCode}&state={validState}");
        request.Headers.Add("Cookie", $"{AuthController.StateCookieName}={validState}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var sessionCookie = setCookies.FirstOrDefault(c => c.Contains("gh_bot_session"));
        sessionCookie.Should().NotBeNull();

        var cookieHeader = sessionCookie!.ToLowerInvariant();
        cookieHeader.Should().Contain("samesite=none");
        cookieHeader.Should().Contain("secure");
        cookieHeader.Should().Contain("httponly");
    }

    [Fact]
    public async Task OAuthStateCookie_ShouldRetainSameSiteLax_ForCsrfProtection()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("FrontendUrl", "https://git-hub-automation-bot.vercel.app");
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/auth/login");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var stateCookie = setCookies.FirstOrDefault(c => c.Contains(AuthController.StateCookieName));
        stateCookie.Should().NotBeNull();

        var cookieHeader = stateCookie!.ToLowerInvariant();
        cookieHeader.Should().Contain("samesite=lax");
        cookieHeader.Should().NotContain("samesite=none");
        cookieHeader.Should().Contain("httponly");
    }

    [Theory]
    [InlineData("https://git-hub-automation-bot.vercel.app")]
    [InlineData("http://localhost:5173")]
    public async Task Cors_AllowedOrigin_ShouldReturnAllowCredentialsAndOrigin(string origin)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(origin);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeTrue();
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().Should().Be("true");
    }

    [Fact]
    public async Task Cors_DisallowedOrigin_ShouldNotReturnCorsHeaders()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        request.Headers.Add("Origin", "https://malicious-site.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
