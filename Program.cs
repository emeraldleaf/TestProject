// ASP.NET Core application entry point with secure file management configuration
using TestProject;
using TestProject.Services;
using TestProject.Security;

var builder = WebApplication.CreateBuilder(args);

// Configure services using extension method
builder.Services.ConfigureApplicationServices(builder.Configuration);

var app = builder.Build();

// DI PERFORMANCE OPTIMIZATION: Early validation to prevent runtime failures
// Anti-pattern: Lazy service resolution that fails at runtime in production
// Solution: Force resolution of critical services during startup to catch DI issues early
// This prevents the costly scenario where a misconfigured service only fails when first used
using (var scope = app.Services.CreateScope())
{
    // Validate that all critical services can be resolved successfully
    // If any service has missing dependencies or circular references,
    // the application will fail fast during startup rather than at runtime
    var _ = scope.ServiceProvider.GetRequiredService<IFileService>();
    var __ = scope.ServiceProvider.GetRequiredService<ISecurityValidationService>();
    var ___ = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

    // Note: This pattern is especially important for services with complex dependency graphs
    // or when using factory patterns that might hide DI configuration errors
}

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