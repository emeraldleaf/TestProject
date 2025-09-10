using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using TestProject.Security;
using TestProject.Services;

namespace TestProject;

/// <summary>
/// Extension methods for configuring services and middleware
/// </summary>
public static class ProgramExtensions
{
    /// <summary>
    /// Configure application services including security services
    /// </summary>
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure security options
        services.Configure<SecurityOptions>(
            configuration.GetSection(SecurityOptions.SectionName));

        // Register security services
        services.AddSingleton<ISecurityValidationService, SecurityValidationService>();
        
        // Register application services
        services.AddScoped<IFileService, FileSystemService>();
        
        // Add controllers
        services.AddControllers();
        
        // Configure JSON options
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null; // Use PascalCase
        });
        
        // Add CORS if needed
        services.AddCors(options =>
        {
            options.AddPolicy("FileDialogPolicy", policy =>
            {
                policy.WithOrigins("http://localhost:3000", "http://localhost:5120") // Adjust as needed
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        // Add request size limits (cross-platform) handles only multipart form uploads
        // The FormOptions is still needed because it has different validation logic for
        // multipart uploads specifically.
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
        });

        // Configure Kestrel and IIS for self-hosted scenarios
        /*  - Handle all request types at the serverlevel
            - Raw JSON posts, XML, any request body content
        */
        
        //Kestrel
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
        });
        
        //IIS
        services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
        });

        return services;
    }

    /// <summary>
    /// Configure the HTTP request pipeline with security middleware
    /// </summary>
    public static WebApplication ConfigureSecurePipeline(this WebApplication app)
    {
        // Static files should be served from wwwroot by default
        app.UseStaticFiles();
        
        // Security headers and middleware should be early in pipeline
        app.UseMiddleware<SecurityMiddleware>();
        
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            //this isn't setup -- is just a placeholder
            app.UseExceptionHandler("/Error");
            app.UseHsts(); // HTTP Strict Transport Security
        }

        // HTTPS redirection
        app.UseHttpsRedirection();
        
      
        
        // CORS
        app.UseCors("FileDialogPolicy");
        
        // Authentication/Authorization would go here if needed
        // app.UseAuthentication();
        // app.UseAuthorization();
        
  
        app.MapControllers();
        
        return app;
    }
}