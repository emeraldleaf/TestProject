using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TestProject.Security;

/// <summary>
/// Implementation of security validation service with comprehensive input sanitization
/// and security measures for file operations
/// </summary>
public class SecurityValidationService : ISecurityValidationService
{
    private readonly SecurityOptions _options;
    private readonly ILogger<SecurityValidationService> _logger;
    
    // Rate limiting storage - in production, use Redis or distributed cache
    private readonly ConcurrentDictionary<string, List<DateTime>> _rateLimitTracker = new();
    
    // Dangerous file extensions and patterns
    private static readonly string[] DangerousExtensions = 
    {
        ".exe", ".bat", ".cmd", ".com", ".pif", ".scr", ".vbs", ".js", ".jar", 
        ".ps1", ".sh", ".php", ".asp", ".aspx", ".jsp", ".py", ".pl", ".rb"
    };
    
    private static readonly string[] DangerousPatterns = 
    {
        "..", "~", "$", "%", "&", "*", "|", "<", ">", "?", ":", "\"", "\\", "/"
    };
    
    // Regex patterns for validation
    private static readonly Regex PathTraversalPattern = new(@"\.\.[\\/]|[\\/]\.\.", RegexOptions.Compiled);
    private static readonly Regex InvalidFileNamePattern = new(@"[<>:""/\\|?*\x00-\x1f]", RegexOptions.Compiled);
    private static readonly Regex SafeSearchPattern = new(@"^[a-zA-Z0-9\s\-_.]+$", RegexOptions.Compiled);
    
    public SecurityValidationService(IOptions<SecurityOptions> options, ILogger<SecurityValidationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public PathValidationResult ValidateAndSanitizePath(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return new PathValidationResult(false, null, "Path cannot be empty");
        }

        try
        {
            // Remove dangerous characters and normalize
            var sanitized = inputPath.Trim();
            
            // Check for path traversal attempts
            if (PathTraversalPattern.IsMatch(sanitized))
            {
                _logger.LogWarning("Path traversal attempt detected: {Path}", inputPath);
                return new PathValidationResult(false, null, "Invalid path: path traversal detected");
            }
            
            // Check for null bytes and control characters
            if (sanitized.Contains('\0') || sanitized.Any(c => c < 32 && c != '\t'))
            {
                _logger.LogWarning("Null bytes or control characters detected in path: {Path}", inputPath);
                return new PathValidationResult(false, null, "Invalid path: contains illegal characters");
            }
            
            // Normalize path separators and resolve to absolute path
            var normalizedPath = Path.GetFullPath(sanitized);
            
            // Ensure path is within allowed base directory
            var basePath = Path.GetFullPath(_options.AllowedBasePath);
            if (!normalizedPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path outside allowed base directory: {Path}, Base: {Base}", 
                    normalizedPath, basePath);
                return new PathValidationResult(false, null, "Access denied: path outside allowed directory");
            }
            
            // Check path length
            if (normalizedPath.Length > _options.MaxPathLength)
            {
                return new PathValidationResult(false, null, 
                    $"Path too long (max {_options.MaxPathLength} characters)");
            }
            
            return new PathValidationResult(true, normalizedPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating path: {Path}", inputPath);
            return new PathValidationResult(false, null, "Invalid path format");
        }
    }

    public FileUploadValidationResult ValidateFileUpload(IFormFile file, string fileName)
    {
        if (file == null || file.Length == 0)
        {
            return new FileUploadValidationResult(false, null, 0, "No file provided");
        }

        // Validate file size
        if (file.Length > _options.MaxFileSize)
        {
            _logger.LogWarning("File too large: {Size} bytes, Max: {MaxSize}", 
                file.Length, _options.MaxFileSize);
            return new FileUploadValidationResult(false, null, file.Length, 
                $"File too large (max {_options.MaxFileSize / (1024 * 1024)} MB)");
        }

        // Sanitize file name
        var sanitizedFileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            return new FileUploadValidationResult(false, null, file.Length, "Invalid file name");
        }

        // Check for dangerous file extensions
        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
        if (DangerousExtensions.Contains(extension))
        {
            _logger.LogWarning("Dangerous file extension detected: {Extension}", extension);
            return new FileUploadValidationResult(false, null, file.Length, 
                "File type not allowed for security reasons");
        }

        // Check allowed file types
        if (_options.AllowedFileExtensions.Any() && 
            !_options.AllowedFileExtensions.Contains(extension))
        {
            return new FileUploadValidationResult(false, null, file.Length, 
                "File type not in allowed list");
        }

        // Validate MIME type
        if (!IsValidMimeType(file.ContentType, extension))
        {
            _logger.LogWarning("MIME type mismatch: ContentType={ContentType}, Extension={Extension}", 
                file.ContentType, extension);
            return new FileUploadValidationResult(false, null, file.Length, 
                "File content does not match extension");
        }

        // Additional security checks - scan file content for malicious patterns
        if (ContainsMaliciousContent(file))
        {
            _logger.LogWarning("Malicious content detected in uploaded file: {FileName}", fileName);
            return new FileUploadValidationResult(false, null, file.Length, 
                "File contains potentially malicious content");
        }

        return new FileUploadValidationResult(true, sanitizedFileName, file.Length);
    }

    public SearchValidationResult ValidateSearchTerm(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new SearchValidationResult(false, null, "Search term cannot be empty");
        }

        var trimmed = searchTerm.Trim();
        
        // Check length
        if (trimmed.Length > _options.MaxSearchTermLength)
        {
            return new SearchValidationResult(false, null, 
                $"Search term too long (max {_options.MaxSearchTermLength} characters)");
        }

        // Check for dangerous patterns
        if (DangerousPatterns.Any(pattern => trimmed.Contains(pattern)))
        {
            _logger.LogWarning("Dangerous pattern in search term: {SearchTerm}", searchTerm);
            return new SearchValidationResult(false, null, "Search term contains invalid characters");
        }

        // Allow only safe characters (letters, numbers, spaces, hyphens, underscores, dots)
        if (!SafeSearchPattern.IsMatch(trimmed))
        {
            _logger.LogWarning("Invalid characters in search term: {SearchTerm}", searchTerm);
            return new SearchValidationResult(false, null, "Search term contains invalid characters");
        }

        // HTML encode to prevent XSS if term is displayed
        var sanitized = System.Net.WebUtility.HtmlEncode(trimmed);
        
        return new SearchValidationResult(true, sanitized);
    }

    public bool IsWithinRateLimit(string clientId, string operation)
    {
        var key = $"{clientId}:{operation}";
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-_options.RateLimitWindowMinutes);

        // Get or create tracking list for this client/operation
        var requests = _rateLimitTracker.GetOrAdd(key, _ => new List<DateTime>());

        lock (requests)
        {
            // Remove old requests outside the time window
            requests.RemoveAll(req => req < windowStart);
            
            // Check if we're at the limit
            if (requests.Count >= _options.MaxRequestsPerWindow)
            {
                _logger.LogWarning("Rate limit exceeded for client {ClientId}, operation {Operation}", 
                    clientId, operation);
                return false;
            }
            
            // Add current request
            requests.Add(now);
            return true;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        // Remove invalid characters
        var sanitized = InvalidFileNamePattern.Replace(fileName, "_");
        
        // Remove leading/trailing dots and spaces
        sanitized = sanitized.Trim().Trim('.');
        
        // Ensure it's not empty and not a reserved name
        if (string.IsNullOrWhiteSpace(sanitized) || IsReservedFileName(sanitized))
        {
            // Preserve the extension when replacing reserved names
            var originalExtension = Path.GetExtension(fileName);
            sanitized = $"file_{DateTime.UtcNow:yyyyMMdd_HHmmss}{originalExtension}";
        }
        
        // Limit length
        if (sanitized.Length > 255)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExt[..(250 - extension.Length)] + extension;
        }

        return sanitized;
    }

    private static bool IsReservedFileName(string fileName)
    {
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", 
                              "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", 
                              "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", 
                              "LPT7", "LPT8", "LPT9" };
        
        return reserved.Contains(Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant());
    }

    private static bool IsValidMimeType(string contentType, string extension)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        // Basic MIME type validation - expand as needed
        var validMimeTypes = new Dictionary<string, string[]>
        {
            { ".txt", new[] { "text/plain" } },
            { ".pdf", new[] { "application/pdf" } },
            { ".jpg", new[] { "image/jpeg" } },
            { ".jpeg", new[] { "image/jpeg" } },
            { ".png", new[] { "image/png" } },
            { ".gif", new[] { "image/gif" } },
            { ".zip", new[] { "application/zip" } },
            { ".doc", new[] { "application/msword" } },
            { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } }
        };

        return validMimeTypes.TryGetValue(extension, out var validTypes) && 
               validTypes.Contains(contentType.ToLowerInvariant());
    }

    private bool ContainsMaliciousContent(IFormFile file)
    {
        try
        {
            // Read first few KB to scan for malicious patterns
            const int scanSize = 8192; // 8KB
            var buffer = new byte[Math.Min(scanSize, file.Length)];
            
            using var stream = file.OpenReadStream();
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            // Check for common malicious patterns
            var maliciousPatterns = new[]
            {
                "<script", "javascript:", "vbscript:", "onload=", "onerror=",
                "<?php", "<%", "eval(", "exec(", "system(",
                "cmd.exe", "powershell", "/bin/sh"
            };

            return maliciousPatterns.Any(pattern => 
                content.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning file content for malicious patterns");
            return true; // Err on the side of caution
        }
    }
}

/// <summary>
/// Configuration options for security validation
/// </summary>
public class SecurityOptions
{
    public const string SectionName = "Security";
    
    /// <summary>
    /// Base directory that all file operations must be within
    /// </summary>
    public string AllowedBasePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    
    /// <summary>
    /// Maximum allowed file size in bytes (default: 10MB)
    /// </summary>
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024;
    
    /// <summary>
    /// Maximum path length
    /// </summary>
    public int MaxPathLength { get; set; } = 260;
    
    /// <summary>
    /// Maximum search term length
    /// </summary>
    public int MaxSearchTermLength { get; set; } = 100;
    
    /// <summary>
    /// Allowed file extensions (empty means allow all except dangerous ones)
    /// </summary>
    public string[] AllowedFileExtensions { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Rate limiting - maximum requests per window
    /// </summary>
    public int MaxRequestsPerWindow { get; set; } = 100;
    
    /// <summary>
    /// Rate limiting - time window in minutes
    /// </summary>
    public int RateLimitWindowMinutes { get; set; } = 15;
}