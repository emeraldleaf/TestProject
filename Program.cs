// ASP.NET Core application entry point with secure file management configuration
using TestProject;

var builder = WebApplication.CreateBuilder(args);

// Configure services using extension method
builder.Services.ConfigureApplicationServices(builder.Configuration);

var app = builder.Build();

// Configure security middleware and request pipeline
app.ConfigureSecurePipeline();

// Configure development-specific middleware after pipeline
if (app.Environment.IsDevelopment())
{
    // Disable caching in development
    app.Use(async (context, next) =>
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        await next();
    });
}

// Prevent HTML caching for development hot-reload
app.Use(async (context, next) =>
{
    await next();
    
    if (context.Request.Path.StartsWithSegments("/src") && 
        context.Request.Path.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        context.Response.Headers["Last-Modified"] = DateTimeOffset.UtcNow.ToString("R");
    }
});

app.Run();

/// <summary>
/// Program entry point partial class for integration tests and WebApplicationFactory discovery.
/// </summary>
public partial class Program { }