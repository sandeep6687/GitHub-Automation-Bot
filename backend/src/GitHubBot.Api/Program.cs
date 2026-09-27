using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Interfaces;
using GitHubBot.Domain.Logic;
using GitHubBot.Infrastructure.ActionHandlers;
using GitHubBot.Infrastructure.BackgroundWorkers;
using GitHubBot.Infrastructure.ExternalServices;
using GitHubBot.Infrastructure.Persistence;
using GitHubBot.Infrastructure.Persistence.Repositories;
using GitHubBot.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 0. Support dynamic PORT binding for deployment platforms (Render, Railway, Cloud Run, Heroku)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// 1. Database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = ResolveDatabaseConnectionString(builder.Configuration);
    options.UseNpgsql(connectionString);
});

// 2. Security & Encryption
builder.Services.AddSingleton<ITokenEncryptionService, TokenEncryptionService>();

// 3. Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IConnectedRepositoryRepository, ConnectedRepositoryRepository>();
builder.Services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
builder.Services.AddScoped<IRuleRepository, RuleRepository>();
builder.Services.AddScoped<IActionExecutionRepository, ActionExecutionRepository>();

// 4. External Clients
builder.Services.AddHttpClient<IGitHubOAuthClient, GitHubOAuthClient>();
builder.Services.AddHttpClient<IGitHubApiClient, GitHubApiClient>();
builder.Services.AddScoped<IGitHubTokenProvider, GitHubTokenProvider>();
builder.Services.Configure<GitHubBot.Application.Configuration.SlackOptions>(
    builder.Configuration.GetSection(GitHubBot.Application.Configuration.SlackOptions.SectionName));
builder.Services.AddHttpClient<ISlackApiClient, SlackApiClient>();

// 5. Action Handlers & Dispatcher
builder.Services.AddScoped<IActionHandler, GitHubLabelActionHandler>();
builder.Services.AddScoped<IActionHandler, GitHubCommentActionHandler>();
builder.Services.AddScoped<IActionHandler, SlackNotificationActionHandler>();
builder.Services.AddHttpClient<AiTriageActionHandler>();
builder.Services.AddScoped<IActionHandler, AiTriageActionHandler>();
builder.Services.AddScoped<IActionDispatcher, ActionDispatcher>();

// 6. Application Services & Rule Engine
builder.Services.AddSingleton<RuleEngine>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IWebhookSignatureValidator, WebhookSignatureValidator>();
builder.Services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
var webhookUrl = builder.Configuration["Webhooks:PublicUrl"]
    ?? builder.Configuration["GitHub:WebhookBaseUrl"]
    ?? "http://localhost:5000/api/webhooks/github";

builder.Services.AddScoped<IRepositoryService>(sp =>
    new RepositoryService(
        sp.GetRequiredService<IUserRepository>(),
        sp.GetRequiredService<IConnectedRepositoryRepository>(),
        sp.GetRequiredService<IGitHubApiClient>(),
        sp.GetRequiredService<ITokenEncryptionService>(),
        webhookUrl));

builder.Services.AddScoped<IEventProcessor, RuleExecutionProcessor>();
builder.Services.AddScoped<IEventProcessingService, EventProcessingService>();

// 7. Background Worker
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.AddHostedService<EventProcessingWorker>();

// 8. Forwarded Headers for Reverse Proxy / HTTPS Deployment
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// 9. Authentication & Session Cookies
//
// Architecture note: in production the Vercel CDN proxies /api/* to this backend
// server-side, so the browser always talks to git-hub-automation-bot.vercel.app.
// That makes the session cookie same-site (Lax is sufficient; SameSite=None is
// not needed and not used). appsettings.Production.json sets CookieSameSite=Lax.
//
// In local development the Vite dev-server proxy forwards /api/* to localhost:5000,
// same-origin for cookie purposes, so Lax is also correct there.
//
// The explicit Authentication:CookieSameSite config value always wins. The default
// fallback is Lax. SameSite=None is never auto-applied — it must be set explicitly
// in config if ever needed for a different deployment topology.
var cookieName = builder.Configuration["Authentication:CookieName"] ?? "gh_bot_session";
var sessionMinutes = builder.Configuration.GetValue("Authentication:SessionExpirationMinutes", 1440);
var frontendUrl = builder.Configuration["FrontendUrl"];

var sameSiteConfig = builder.Configuration["Authentication:CookieSameSite"];
var sameSiteMode = !string.IsNullOrEmpty(sameSiteConfig) && Enum.TryParse<SameSiteMode>(sameSiteConfig, true, out var parsedSameSite)
    ? parsedSameSite
    : SameSiteMode.Lax; // Default: Lax. Override via Authentication:CookieSameSite if needed.

var securePolicyConfig = builder.Configuration["Authentication:CookieSecurePolicy"];
var securePolicy = !string.IsNullOrEmpty(securePolicyConfig) && Enum.TryParse<CookieSecurePolicy>(securePolicyConfig, true, out var parsedSecurePolicy)
    ? parsedSecurePolicy
    : CookieSecurePolicy.SameAsRequest; // Always in production via appsettings.Production.json

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = sameSiteMode;
        options.Cookie.SecurePolicy = securePolicy;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(sessionMinutes);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddScoped<IRuleService, RuleService>();
builder.Services.AddScoped<IActivityService, ActivityService>();

// 10. CORS Configuration (allows configured origins and automatically includes FrontendUrl)
var defaultOrigins = new[]
{
    "http://localhost:5173",
    "http://127.0.0.1:5173",
    "http://localhost:3000",
    "https://git-hub-automation-bot.vercel.app"
};
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? defaultOrigins;
var originSet = new HashSet<string>(configuredOrigins, StringComparer.OrdinalIgnoreCase);

// Ensure essential production and development origins are always registered
originSet.Add("https://git-hub-automation-bot.vercel.app");
originSet.Add("http://localhost:5173");
originSet.Add("http://127.0.0.1:5173");
originSet.Add("http://localhost:3000");

if (!string.IsNullOrWhiteSpace(frontendUrl))
{
    originSet.Add(frontendUrl.TrimEnd('/'));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(originSet.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// Safe optional automatic migration on startup
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup", false) ||
    builder.Configuration.GetValue<bool>("APPLY_MIGRATIONS_ON_STARTUP", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Response buffering ensures StreamPipeWriter compatibility across hosts
app.Use(async (context, next) =>
{
    var originalBody = context.Response.Body;
    await using var memStream = new MemoryStream();
    context.Response.Body = memStream;

    await next();

    memStream.Position = 0;
    await memStream.CopyToAsync(originalBody);
});

app.UseForwardedHeaders();
app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program
{
    private static string ResolveDatabaseConnectionString(IConfiguration config)
    {
        var raw = config.GetConnectionString("Database")
            ?? config["DATABASE_URL"]
            ?? "Host=localhost;Port=5432;Database=github_bot;Username=postgres;Password=postgres";

        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo[0];
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var hostPort = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.TrimStart('/');
            return $"Host={host};Port={hostPort};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
        }

        return raw;
    }
}
