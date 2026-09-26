var builder = WebApplication.CreateBuilder(args);

// --- Service registration (Phase 1+) ---

var app = builder.Build();

// --- Middleware pipeline (Phase 2+) ---

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
