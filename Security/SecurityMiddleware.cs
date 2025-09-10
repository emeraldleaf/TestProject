using Microsoft.Extensions.Options;
using System.Net;

namespace TestProject.Security;

/// <summary>
/// Security middleware to apply global security measures to all requests
/// </summary>
public class SecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityOptions _options;
    private readonly ILogger<SecurityMiddleware> _logger;
    private readonly ISecurityValidationService _securityService;

    public SecurityMiddleware(
        RequestDelegate next, 
        IOptions<SecurityOptions> options,
        ILogger<SecurityMiddleware> logger,
        ISecurityValidationService securityService)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
        _securityService = securityService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get client IP for rate limiting
        var clientIp = GetClientIpAddress(context);
        var path = context.Request.Path.Value?.ToLowerInvariant();
        
        // Apply rate limiting for file operations
        if (IsFileOperation(path))
        {
            var operation = GetOperationType(path, context.Request.Method);
            
            if (!_securityService.IsWithinRateLimit(clientIp, operation))
            {
                _logger.LogWarning("Rate limit exceeded for IP {ClientIp} on operation {Operation}", 
                    clientIp, operation);
                
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
                return;
            }
        }

        // Add security headers
        AddSecurityHeaders(context.Response);
        
        // Check request size for uploads
        if (context.Request.Method == "POST" && context.Request.ContentLength > _options.MaxFileSize)
        {
            _logger.LogWarning("Request too large: {Size} bytes from IP {ClientIp}", 
                context.Request.ContentLength, clientIp);
            
            context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
            await context.Response.WriteAsync("Request too large");
            return;
        }

        // Log potentially suspicious requests
        if (IsSuspiciousRequest(context.Request))
        {
            _logger.LogWarning("Suspicious request detected from IP {ClientIp}: {Path}", 
                clientIp, context.Request.Path);
        }

        await _next(context);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Try to get real IP from headers (for load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool IsFileOperation(string? path)
    {
        return path?.StartsWith("/api/files") == true;
    }

    private static string GetOperationType(string? path, string method)
    {
        return path switch
        {
            var p when p?.Contains("/upload") == true => "upload",
            var p when p?.Contains("/download") == true => "download",
            var p when p?.Contains("/search") == true => "search",
            _ => method.ToLowerInvariant()
        };
    }

    private static void AddSecurityHeaders(HttpResponse response)
    {
        // Prevent clickjacking
        response.Headers.Append("X-Frame-Options", "DENY");
        
        // Prevent MIME type sniffing
        response.Headers.Append("X-Content-Type-Options", "nosniff");
        
        // XSS protection
        response.Headers.Append("X-XSS-Protection", "1; mode=block");
        
        // Referrer policy
        response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        
        // Content Security Policy (adjust as needed)
        response.Headers.Append("Content-Security-Policy", 
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'");
    }

    private static bool IsSuspiciousRequest(HttpRequest request)
    {
        var path = request.Path.Value?.ToLowerInvariant();
        var query = request.QueryString.Value?.ToLowerInvariant();
        
        // Check for common attack patterns
        var suspiciousPatterns = new[]
        {
            "../", "..\\", "%2e%2e", "%252e%252e",
            "<script", "javascript:", "vbscript:",
            "union select", "or 1=1", "' or '1'='1",
            "/etc/passwd", "/proc/", "cmd.exe", "powershell"
        };

        var fullRequest = $"{path} {query}";
        
        return suspiciousPatterns.Any(pattern => 
            fullRequest?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true);
    }
}