using GitHubBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register PostgreSQL DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Database")
        ?? "Host=localhost;Port=5432;Database=github_bot;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
