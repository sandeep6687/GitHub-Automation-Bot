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
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Database")
        ?? "Host=localhost;Port=5432;Database=github_bot;Username=postgres;Password=postgres";
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

// 6. Background Worker
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.AddHostedService<EventProcessingWorker>();

// 7. Authentication & Session Cookies
var cookieName = builder.Configuration["Authentication:CookieName"] ?? "gh_bot_session";
var sessionMinutes = builder.Configuration.GetValue("Authentication:SessionExpirationMinutes", 1440);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
