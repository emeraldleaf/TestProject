using TestProject;

var builder = WebApplication.CreateBuilder(args);

// Configure services using extension method
builder.Services.ConfigureApplicationServices(builder.Configuration);

var app = builder.Build();

// Use the secure pipeline configuration first
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

// Ensure HTML files are never cached (applies to all environments)
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