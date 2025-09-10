using TestProject.Models;

namespace TestProject.Security;

/// <summary>
/// Service interface for handling security validation and sanitization
/// </summary>
public interface ISecurityValidationService
{
    /// <summary>
    /// Validates and sanitizes file paths to prevent path traversal attacks
    /// </summary>
    /// <param name="inputPath">The input path to validate</param>
    /// <returns>Validation result with sanitized path</returns>
    PathValidationResult ValidateAndSanitizePath(string? inputPath);
    
    /// <summary>
    /// Validates file upload security (size, type, content)
    /// </summary>
    /// <param name="file">The uploaded file</param>
    /// <param name="fileName">The desired file name</param>
    /// <returns>Validation result</returns>
    FileUploadValidationResult ValidateFileUpload(IFormFile file, string fileName);
    
    /// <summary>
    /// Validates search terms to prevent injection attacks
    /// </summary>
    /// <param name="searchTerm">The search term to validate</param>
    /// <returns>Validation result with sanitized search term</returns>
    SearchValidationResult ValidateSearchTerm(string? searchTerm);
    
    /// <summary>
    /// Checks if the current operation is within rate limits
    /// </summary>
    /// <param name="clientId">Client identifier (IP or user ID)</param>
    /// <param name="operation">Type of operation</param>
    /// <returns>True if within limits</returns>
    bool IsWithinRateLimit(string clientId, string operation);
}

/// <summary>
/// Result of path validation
/// </summary>
public record PathValidationResult(
    bool IsValid,
    string? SanitizedPath,
    string? ErrorMessage = null
);

/// <summary>
/// Result of file upload validation
/// </summary>
public record FileUploadValidationResult(
    bool IsValid,
    string? SanitizedFileName,
    long FileSize,
    string? ErrorMessage = null
);

/// <summary>
/// Result of search term validation
/// </summary>
public record SearchValidationResult(
    bool IsValid,
    string? SanitizedTerm,
    string? ErrorMessage = null
);